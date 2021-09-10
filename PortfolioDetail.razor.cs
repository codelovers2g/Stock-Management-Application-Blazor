using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Demo.Pages
{
    public partial class PortfolioDetail : ComponentBase
    {
        [Parameter]
        public int PortfolioId { get; set; }
        [Parameter]
        public bool IsPortfolio { get; set; }
        [Inject]
        public IJSRuntime JSRuntime { get; set; }
        [Inject]
        public IPortfolioRepository portfolioRepository { get; set; }
        [Inject]
        public IListingRepository listingRepository { get; set; }
        [Inject]
        public IVaRRepository vaRRepository { get; set; }
        [Inject]
        public IFeaturesManager _featureManager { get; set; }

        public List<VaRModel> VaRDetails;
        public List<VaRChartModel> VaRPortfolioListingChart = new List<VaRChartModel>();
        public List<VaRChartModel> VaRPortfolioChart = new List<VaRChartModel>();
        public List<string> PortfolioListingNames = new List<string>();
        public List<string> PortfolioName = new List<string>();
        public PortfolioDetailDTO PortfolioDetailDto;

        public bool isLoadingData { get; set; }
        public bool _allowVaRChart { get; set; } = false;
        public bool _allowVaRListingChart { get; set; } = false;
        public bool _allowVaRGrid { get; set; } = false;
        public string VaRPortfolioJSFunction = "chartingFunctions.showPortfolioValueChart";
        public string VaRPortfolioListingJSFunction = "vaRChartingFunctions.GetVaRListingChartData";
        public string VaRPortfolioDiv = "var_chart_div";
        public string VaRPortfolioListingDiv = "var_listing_chart_div";
        public string VaRPortfolioListingChartHeight = string.Empty;
        public string VaRPortfolioChartHeight = "195px";

        protected override async Task OnInitializedAsync()
        {
            //Get Feature Data
            await GetFeaturesData();
        }
        /// <summary>
        /// Get Features Data and set into the session storage
        /// </summary>
        public async Task GetFeaturesData()
        {
            var category = IsPortfolio ? FeatureConstants.PortfolioVar.CategoryName : FeatureConstants.ModelVar.CategoryName;
            //Get Feature Value According to Category
            List<CategoryFeatureModel> CategoryFeatures = await _featureManager.GetCategoryFeatures(category);
            //Set Enable or disable feature
            if (CategoryFeatures != null && CategoryFeatures.Count > 0)
            {
                var vaRchart = IsPortfolio ? FeatureConstants.PortfolioVar.PortfolioVaRChart : FeatureConstants.ModelVar.ModelVaRChart;
                var vaRListingChart = IsPortfolio ? FeatureConstants.PortfolioVar.PortfolioVaRListingChart : FeatureConstants.ModelVar.ModelVaRListingChart;
                var vaRGrid = IsPortfolio ? FeatureConstants.PortfolioVar.PortfolioVaRGrid : FeatureConstants.ModelVar.ModelVaRGrid;

                _allowVaRChart = await _featureManager.GetFeatureStatus(vaRchart, CategoryFeatures);
                _allowVaRListingChart = await _featureManager.GetFeatureStatus(vaRListingChart, CategoryFeatures);
                _allowVaRGrid = await _featureManager.GetFeatureStatus(vaRGrid, CategoryFeatures);
            }
        }

        protected override async Task OnParametersSetAsync()
        {
            try
            {
                isLoadingData = true;
                StateHasChanged();
                //Get VaR Grid data
                VaRDetails = await vaRRepository.GetVaRDetail(PortfolioId);
                SetChartHeight();
                SetVaRChartData();
                StateHasChanged();
                //Call Portfolio Chart
                if (VaRPortfolioChart != null && VaRPortfolioChart.Count > 0 && _allowVaRChart)
                {
                    await JSRuntime.InvokeVoidAsync(VaRPortfolioListingJSFunction, new object[] {VaRPortfolioChart, PortfolioName, VaRPortfolioDiv });
                }
                //Call Portfolio Listing Chart
                if (VaRPortfolioListingChart != null && VaRPortfolioListingChart.Count > 0 && _allowVaRListingChart)
                {
                    await JSRuntime.InvokeVoidAsync(VaRPortfolioListingJSFunction, new object[] { VaRPortfolioListingChart, PortfolioListingNames, VaRPortfolioListingDiv });
                }
                isLoadingData = false;
                StateHasChanged();
            }
            catch (Exception exception)
            {
                AppendToLog(exception.ToString());
            }
        }
        /// <summary>
        /// Set VaR Chart Data
        /// </summary>
        public void SetVaRChartData()
        {
            if (VaRDetails != null && VaRDetails.Count > 0)
            {
                //Set Portfolio Listing Chart data
                var filteredVaRListingData = VaRDetails.Where(x => !x.ListingName.Equals(Constants.PORTFOLIO, StringComparison.InvariantCultureIgnoreCase))
                                                .OrderBy(x => x.ListingName).ToList();
                PortfolioListingNames = filteredVaRListingData.Select(x => x.ListingName).ToList();
                VaRPortfolioListingChart = SetChartData(filteredVaRListingData);

                //Set Portfolio Chart data
                var filteredVaRPotrtfolioData = VaRDetails.Where(x => x.ListingName.Equals(Constants.PORTFOLIO, StringComparison.InvariantCultureIgnoreCase))
                                               .OrderBy(x => x.ListingName).ToList();
                PortfolioName = new List<string>() { Constants.MYPORTFOLIO };
                VaRPortfolioChart = SetChartData(filteredVaRPotrtfolioData);

            }
        }
        /// <summary>
        /// Set Chart Data
        /// </summary>
        /// <param name="filteredVaRData"></param>
        /// <returns></returns>
        public List<VaRChartModel> SetChartData(List<VaRModel> filteredVaRData)
        {
            List<VaRChartModel> chartSecondBarData = new List<VaRChartModel>();
            //Check Is this Portfolio Bar chart or not
            var portfolioChart = filteredVaRData.Where(x => x.ListingName.Equals(Constants.PORTFOLIO, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
            //Listing Chart First Bar data
            VaRChartModel chartFirstBarData = new VaRChartModel();
            if (portfolioChart != null)
            {
                chartFirstBarData.Name = Constants.PORTFOLIOVALUE;
            }
            else
                chartFirstBarData.Name = Constants.STOCKVALUE;
            chartFirstBarData.Data = filteredVaRData.Select(x => x.Value).ToList();
            chartFirstBarData.Color = Constants.STOCKVALUECOLOR;
            chartFirstBarData.Stack = Constants.STOCKVALUE;
            chartSecondBarData.Add(chartFirstBarData);

            foreach (var item in VaRPortfoliotxt)
            {
                VaRChartModel chartData = new VaRChartModel();
                chartData.Name = item;
                chartData.Stack = Constants.SECONDBARSTACK;

                switch (item)
                {
                    case Constants.VALUEOFSAFETY:
                        chartData.Data = filteredVaRData.Select(x => x.ValueOfSafety).ToList();
                        chartData.Color = Constants.VALUEOFSAFETYCOLOR;
                        break;
                    case Constants.VALUEATRISK:
                        chartData.Data = filteredVaRData.Select(x => x.ValueAtRisk).ToList();
                        chartData.Color = Constants.VALUEATRISKCOLOR;
                        break;
                    case Constants.EXPECTEDRETURN:
                        chartData.Data = filteredVaRData.Select(x => x.ExpectedReturn).ToList();
                        chartData.Color = Constants.EXPECTEDRETURNCOLOR;
                        break;
                    default:
                        break;
                }
                chartSecondBarData.Add(chartData);
            }
            return chartSecondBarData;
        }
        /// <summary>
        /// Set Height of the chart according to the Listings
        /// </summary>
        public void SetChartHeight()
        {
            VaRPortfolioListingChartHeight = $"{VaRDetails.Count * 40 + 150}px";//Set Height of the Sankey Div
        }
        /// <summary>
        /// Show Error message
        /// </summary>
        /// <param name="message"></param>
        void AppendToLog(string message)
        {
            string htmlMessage = $"<br />Exception Ocurred: {message}";
            logger = new MarkupString(logger + htmlMessage);
        }
    }
}
