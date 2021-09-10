
1. PortfolioDetail.js - This will contain the implementation of Var chart and Sankey Chart (we have used Highchart chart). In the Sankey Chart 
we have called another chart(Series Chart) after clicking on nodes of the Sankey chart. We have also set the colors, custom labels and custom tooltip (on hover) 
for each nodes.

3.StockQuoteDailyTrigger.cs (Azure function) - In this we are using Azure Durable functions, in which we are triggering Azure Function(or Calculation) on a particular time 
or through Http triggers. We are getting the stocks data on daily basis by time trigger. We are also triggering the Azure function from our Web App using Http or Queue trigger 
according to our requirement.

3. Wikipedia : We are using wikipedia api to implement Wikipedia in Bagel App. From this we can get definition of any word related to Stock Market.




