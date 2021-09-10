using aspnet_core_dotnet_core.Domain.Models;
using EFCore.BulkExtensions;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using TimeDurableFunction.Helper;
using TimeDurableFunction.InitialDataLoaderHttpTriggers;
using TimeDurableFunction.Models;
using TimeDurableFunction.SystemConstants;

namespace TimeDurableFunction
{
    // this function is used for fetching EOD data from api
    public class StockQuoteAlphaVantageDailyTrigger
    {
        private readonly BagelAppdbContext _context;
        private readonly AlphaVantageApiClient _client;
        private JsonSerializerOptions defaultJsonSerializerOptions =>
             new JsonSerializerOptions() { PropertyNameCaseInsensitive = true };

        public StockQuoteAlphaVantageDailyTrigger(BagelAppdbContext context, AlphaVantageApiClient client)
        {
            _context = context;
            _client = client;
        }

        [FunctionName(StockQuoteAlphaVantageDailyTriggerConstants.Orchestrator)]
        public async Task RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
        {
            const int numberOfSubOrchestrators = 2;
            const int batchSize = 400;//always double of subBatchsize
            const int subBatchSize = 200;

            RetryOptions retryOption = new RetryOptions(firstRetryInterval: TimeSpan.FromSeconds(10), maxNumberOfAttempts: 4)
            {
                Handle = (exception) =>
                {
                    //log.LogError(exception, "retry stock quote daily alpha vantage orchestrator called");
                    return exception.InnerException is TooManyRequestsException;
                }
            };
            retryOption.BackoffCoefficient = 2.0;


            var totalListings = await context.CallActivityAsync<int>(StockQuoteAlphaVantageDailyTriggerConstants.GetAlphaEODListingsCount, null);

            var numberOfBatches = (int)Math.Ceiling((double)totalListings / batchSize);
            for (int i = 0; i < numberOfBatches; i++)
            {
                var currentListing = await context.CallActivityAsync<IEnumerable<ListingStatus>>(StockQuoteAlphaVantageDailyTriggerConstants.GetAlphaEODListings, new Page(i * batchSize, batchSize));
                var parallelTasks = new List<Task>();
                for (var n = 0; n < numberOfSubOrchestrators; n++)
                {
                    var subCurrentListing = currentListing.Skip(n * subBatchSize).Take(subBatchSize).ToList();
                    var t = context.CallSubOrchestratorWithRetryAsync(StockQuoteAlphaVantageDailyTriggerConstants.ProcessListingAlphaEODBatchOrchestrator, retryOption, subCurrentListing);
                    parallelTasks.Add(t);
                }
                await Task.WhenAll(parallelTasks);
            }

           await context.CallActivityAsync<bool>(StockQuoteAlphaVantageDailyTriggerConstants.AddMessageInStockQueueForAlphaDaily, null);
           await context.CallActivityAsync<bool>(StockQuoteAlphaVantageDailyTriggerConstants.AddMessageInExchangeItemForAlphaDaily, null);
        }

        [FunctionName(StockQuoteAlphaVantageDailyTriggerConstants.ProcessListingAlphaEODBatchOrchestrator)]
        public async Task SubOrchestrator([OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
        {
            try
            {
                var retryOption = new RetryOptions(firstRetryInterval: TimeSpan.FromMinutes(1), maxNumberOfAttempts: 8)
                {
                    Handle = (exception) =>
                    {
                        return exception.InnerException is TooManyRequestsException;
                    }
                };
                retryOption.BackoffCoefficient = 2.0;

                var listingsSubOrchestrationResult = new List<ListingStatus>();
                var listings = context.GetInput<IEnumerable<ListingStatus>>();

                var result = new List<ListingStatus>();
                var batchSize = 20;
                int numberOfBatches = (int)Math.Ceiling((double)listings.Count() / batchSize);
                for (int i = 0; i < numberOfBatches; i++)
                {
                    var currentListings = listings.Skip(i * batchSize).Take(batchSize).ToList();

                    var tasks = currentListings.Select(lst => context.CallActivityWithRetryAsync<ListingStatus>(StockQuoteAlphaVantageDailyTriggerConstants.FetchListingAlphaEOD, retryOption, lst));
                    listingsSubOrchestrationResult.AddRange(await Task.WhenAll(tasks));
                }

                await context.CallActivityAsync(StockQuoteAlphaVantageDailyTriggerConstants.SaveStockAlphaEODDataInDB, listingsSubOrchestrationResult);
            }
            catch (Exception exception)
            {
                if (exception.InnerException!=null && exception.InnerException is TooManyRequestsException)
                {
                    throw exception.InnerException;

                }
                else
                {
                    var listings = context.GetInput<IEnumerable<ListingStatus>>();
                    log.LogError(exception, $"Exception {exception.Message} for batch listingids {string.Join(',', listings.Select(s => s.ListingId))}");

                }

            }

        }

        [FunctionName(StockQuoteAlphaVantageDailyTriggerConstants.AddMessageInStockQueueForAlphaDaily)]
        public async Task<bool> AddMessageInStockQueueForAlphaDaily([ActivityTrigger] object obj, [Queue("stocklisting-items")] IAsyncCollector<ListingStatus> stockListingItems, ILogger log)
        {
            var maxDate = await _context.StockQuote.MaxAsync(d => d.Date);
            var listing = new ListingStatus()
            {
                DailyData = true,
                ApiResponseStatusId = 1,
                MaxDate = maxDate
            };

            await stockListingItems.AddAsync(listing);
            return listing.DailyData;
        }
        [FunctionName(StockQuoteAlphaVantageDailyTriggerConstants.AddMessageInExchangeItemForAlphaDaily)]
        public async Task<bool> AddMessageInExchangeItemForAlphaDaily([ActivityTrigger] object obj, [Queue("exchange-items")] IAsyncCollector<ExchangeItem> exchangeItems, ILogger log)
        {
            var maxDate = await _context.StockQuote.MaxAsync(d => d.Date);
            var exchangeItem = new ExchangeItem()
            {
                DailyData = true,
                ApiResponseStatusId = 1,
                MaxDate = maxDate
            };

            await exchangeItems.AddAsync(exchangeItem);
            return exchangeItem.DailyData;
        }

        [FunctionName(StockQuoteAlphaVantageDailyTriggerConstants.FetchListingAlphaEOD)]
        public async Task<ListingStatus> FetchListingEODFromApi([ActivityTrigger] ListingStatus listing, [Queue("adjustedclose-items")] IAsyncCollector<ListingAdjustedCloseUpdateStatus> adjustedCloseItem, ILogger log)
        {
            var stockQuote = new StockQuote();
            Listing currentListing = new Listing();
            stockQuote.ListingId = listing.ListingId;
            var encodedSymbol = HttpUtility.UrlEncode(listing.Symbol);

            var url = $"******************************************************************************************";
            var httpResponse = await _client.HttpClient.GetAsync(url);
            if (httpResponse.IsSuccessStatusCode)
            {
                try
                {

                    currentListing = await _context.Listing.Where(lt => lt.ListingId == listing.ListingId).FirstOrDefaultAsync();
                    var response = await Deserialize<EODStockRoot2>(httpResponse, defaultJsonSerializerOptions);

                    if (response != null)
                    {
                        if (response.ErrorMessage != null)
                        {

                            stockQuote.Date = DateTime.Now;
                            listing.Message = $"Response{response.ErrorMessage}";
                            listing.Status = false;
                            listing.StockQuote = stockQuote;

                            listing.LastSuccessfulPull = listing.LastSuccessfulPull;
                            listing.LastTryToPull = DateTime.Now;

                            currentListing.ApiResponseStatusId = 2;
                            _context.Listing.Update(currentListing);
                            await _context.SaveChangesAsync();

                            return listing;
                        }
                        if (response.Information != null)
                        {
                            log.LogError(listing.Symbol + "****** Information *******" + httpResponse.StatusCode);
                            throw new TooManyRequestsException();

                        }

                        var itemList = response.TimeSeriesDaily.ToList();
                        if (itemList == null)
                        {
                            throw new TooManyRequestsException();
                        }
                        var item = itemList.FirstOrDefault();

                        if (item.Value != null)
                        {

                            var listingDate = DateTime.Parse(item.Key);
                            var currentTime = DateTime.Now;
                            if (listingDate.Date == currentTime.Date)
                            {
                                stockQuote = new StockQuote
                                {
                                    ListingId = listing.ListingId,
                                    Date = DateTime.Parse(item.Key),
                                    Close = Math.Round(item.Value.Close, 6),
                                    High = Math.Round(item.Value.High, 6),
                                    Open = Math.Round(item.Value.Open, 6),
                                    AdjustedClose = Math.Round(item.Value.AdjustedClose, 6),
                                    Low = Math.Round(item.Value.Low, 6),
                                    Volume = (long)item.Value.Volume,
                                    SplitCoefficient = Math.Round(item.Value.SplitCoefficient, 6),
                                    DividendAmount = Math.Round(item.Value.DividendAmount, 6),


                                };

                                listing.Message = StockQuoteAlphaVantageDailyTriggerConstants.SuccessMessage;
                                listing.Status = true;
                                listing.LastTryToPull = DateTime.Now;
                                listing.LastSuccessfulPull = DateTime.Now;


                                listing.StockQuote = stockQuote;
                                if (stockQuote.SplitCoefficient != 1)
                                {
                                    var listingAdjustedCloseItem = new ListingAdjustedCloseUpdateStatus()
                                    {

                                        ListingId = listing.ListingId,
                                        ApiResponseStatusId = listing.ApiResponseStatusId,
                                        Symbol = listing.Symbol

                                    };
                                    await adjustedCloseItem.AddAsync(listingAdjustedCloseItem);


                                }
                                return listing;
                            }
                            else
                            {

                                stockQuote.Date = DateTime.Now;
                                listing.StockQuote = stockQuote;
                                listing.LastSuccessfulPull = listing.LastSuccessfulPull;
                                listing.LastTryToPull = DateTime.Now;
                                listing.Message = $"Response Data was not for the current day. Response Date {listingDate.Date}";
                                listing.Status = false;
                                return listing;
                            }
                        }
                        else
                        {
                            currentListing.ApiResponseStatusId = 2;
                            _context.Listing.Update(currentListing);
                            await _context.SaveChangesAsync();
                            listing.Message = "Too many digits for AdjustedClose AlphaVantage";
                            listing.Status = false;
                            listing.LastSuccessfulPull = listing.LastSuccessfulPull;
                            listing.LastTryToPull = DateTime.Now;

                            listing.StockQuote = stockQuote;
                            return listing;
                        }
                    }
                    else
                    {
                        stockQuote.Date = DateTime.Now;
                        listing.StockQuote = stockQuote;
                        listing.Message = $"Response source was  null";
                        listing.LastSuccessfulPull = listing.LastSuccessfulPull;
                        listing.LastTryToPull = DateTime.Now;
                        listing.Status = false;
                        return listing;
                    }

                }

                catch (TooManyRequestsException exception)
                {
                    throw;
                }
                catch (ArgumentNullException argumentNullException) {
                    log.LogError(argumentNullException,listing.Symbol);

                    throw new TooManyRequestsException();

                }
                catch (Exception ex)
                {
                    stockQuote.Date = DateTime.Now;
                    listing.Message = ex.ToString();
                    listing.LastSuccessfulPull = listing.LastSuccessfulPull;
                    listing.LastTryToPull = DateTime.Now;
                    listing.Status = false;
                    log.LogError(listing.Symbol + " *** " + ex.ToString());

                    listing.StockQuote = stockQuote;
                    return listing;
                }
            }
            else if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests)
            {
                log.LogError(listing.Symbol + "------------------------------------------" + httpResponse.StatusCode);
                throw new TooManyRequestsException();
            }
            else
            {
                stockQuote.Date = DateTime.Now;
                listing.StockQuote = stockQuote;
                listing.Message = $"Response { httpResponse.StatusCode}";
                listing.Status = false;
                listing.LastSuccessfulPull = listing.LastSuccessfulPull;
                listing.LastTryToPull = DateTime.Now;

                return listing;
            }
        }
        [FunctionName(StockQuoteAlphaVantageDailyTriggerConstants.SaveStockAlphaEODDataInDB)]
        public async Task SaveStockDataInDB([ActivityTrigger] IEnumerable<ListingStatus> listingsDataFromOrchestrator, [Queue("updatedb-items")] IAsyncCollector<ListingStatus> updatedbItems, ILogger log)
        {
            try
            {
                List<StockQuote> stockQuotes = new List<StockQuote>();
                var listingsData = listingsDataFromOrchestrator.Where(le => le.Status).ToList();
                foreach (var item in listingsData)
                {
                    stockQuotes.Add(item.StockQuote);
                }
                var bulkConfig = new BulkConfig
                {
                    UpdateByProperties = new List<string> { nameof(StockQuote.ListingId), nameof(StockQuote.Date) }
                };


                var listingsToUpdate = listingsDataFromOrchestrator.Select(s => new Listing { 
                    ListingId=s.ListingId,
                    LastTryToPull=s.LastTryToPull,
                    LastSuccessFulPull=s.LastSuccessfulPull,
                    Message=s.Message,
                    Status=s.Status
                }).ToList();

                var listingBulkConfig = new BulkConfig
                {
                    UpdateByProperties = new List<string> { nameof(Listing.ListingId) },
                    PropertiesToInclude = new List<string> {
                                   nameof(Listing.LastSuccessFulPull),
                                   nameof(Listing.LastTryToPull),
                                   nameof(Listing.Status),
                                   nameof(Listing.Message),
                                   
                               }
                };

                await _context.BulkInsertOrUpdateAsync(stockQuotes, bulkConfig);
                await _context.BulkUpdateAsync(listingsToUpdate,listingBulkConfig);

                
            }
            catch (Exception exception)
            {
                log.LogError(exception, $"{StockQuoteAlphaVantageDailyTriggerConstants.SaveStockAlphaEODDataInDB} {exception.Message}");
            }
        }
        private async Task<T> Deserialize<T>(HttpResponseMessage httpResponse, JsonSerializerOptions options)
        {
            var responseString = await httpResponse.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(responseString, options);
        }

        [FunctionName(StockQuoteAlphaVantageDailyTriggerConstants.GetAlphaEODListings)]
        public async Task<IEnumerable<ListingStatus>> GetListingsAsync([ActivityTrigger] Page page, ILogger log)
        {
            // 1 for alpha vantage api
            var listing = await _context.Listing.Where(lt => lt.ApiResponseStatusId == 1).Select(sk => new ListingStatus
            {
                ListingId = sk.ListingId,
                Symbol = sk.ConvertedSymbol,
                ApiResponseStatusId = sk.ApiResponseStatusId.GetValueOrDefault(),
                LastSuccessfulPull=sk.LastSuccessFulPull

            }).OrderBy(st => st.ListingId).Skip(page.Skip).Take(page.Take).ToListAsync();
            return listing;
        }
        [FunctionName(StockQuoteAlphaVantageDailyTriggerConstants.GetAlphaEODListingsCount)]
        public async Task<int> GetEODListingsCountAsync([ActivityTrigger] object maxRecord, ILogger log)
        {
            // 1 for alpha vantage
            return await _context.Listing.Where(lt => lt.ApiResponseStatusId == 1).CountAsync();
        }

        [FunctionName(StockQuoteAlphaVantageDailyTriggerConstants.TimerStart)]
        public async Task Run(
              [TimerTrigger(00 00 20  1-5)] TimerInfo myTimer,
            //[HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter, ILogger log)
        {
            log.LogInformation($"C# Timer trigger TimeTrigger executed at: {DateTime.Now}");
            string instanceId = await starter.StartNewAsync(StockQuoteAlphaVantageDailyTriggerConstants.Orchestrator, null);
            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            //  return starter.CreateCheckStatusResponse(req, instanceId);
        }
        [FunctionName(StockQuoteAlphaVantageDailyTriggerConstants.HttpStart)]
        public async Task<HttpResponseMessage> StockQuoteAlphaVantageDailyTriggerHttpTrigger(
           [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestMessage req,
           [DurableClient] IDurableOrchestrationClient starter)
        {
            string instanceId = await starter.StartNewAsync(StockQuoteAlphaVantageDailyTriggerConstants.Orchestrator, null);

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
    }
}



