const MYPORTFOLIO = "My Portfolio";

window.vaRChartingFunctions = {
    GetVaRListingChartData: function (chartData, listingNames, chartDiv) {

        //This is because we are providing width to the portfolio chart 
        //we don't need width in portfolio listing chart (width property effects portfolio listing chart)
        var data = [];
        var nodeValues = chartData.map(function (x) {
            if (listingNames != null && listingNames != undefined && listingNames[0] != MYPORTFOLIO)
                return data.push({ name: x.name, data: x.data, color: x.color, stack: x.stack });
            else
                return data.push(x);
        });

        Highcharts.chart(chartDiv, {
            chart: {
                type: 'bar',
                marginTop: 50,
                marginLeft: 200
            },
            stockTools: {
                gui: {
                    enabled: false // disable the built-in toolbar
                }
            },
            title: {
                text: ''
            },
            legend: {
                reversed: true
            },
            xAxis: {
                categories: listingNames,
                labels: {
                    useHTML: true,
                }
            },
            yAxis: {
                min: 0,
                title: {
                    text: ''
                }
            },

            plotOptions: {
                series: {
                    stacking: 'normal',
                    groupPadding: 0.1,
                    pointPadding: 0,
                }
            },
            series: data
        });
    },
    //Show Sankey Chart
    showSankeyHighChart: function (chartdivname, correlationValues, trends, legendHeight, dotNetReference) {

        var colors = correlationValues.map(function (x) {
            return x[0];
        });

        //test
        if ($('#Diversification_sankey').length) {
            Highcharts.chart(chartdivname, {
                stockTools: {
                    gui: {
                        enabled: false // disable the built-in toolbar
                    }
                },
                title: {
                    text: ' '
                },
                colors: colors,
                chart: {
                    marginTop: 50
                },

                legend: {
                    align: 'right',
                    layout: 'vertical',
                    margin: 10,
                    verticalAlign: 'top',
                    y: 40,
                    width: '9%',
                    symbolHeight: 17,
                    symbolRadius: 0
                },
                colorAxis: {
                    min: 1,
                    max: 8,
                    tickInterval: 1,
                    dataClasses: [{
                        name: '3 Weeks Up',
                        color: '#005500'
                    }, {
                        name: '2 Weeks Up',
                        color: '#008200'
                    }, {
                        name: '1 Weeks Up',
                        color: '#00E600'
                    }, {
                        name: 'Neutral',
                        color: '#FFFF32'
                    }, {
                        name: '1 Weeks Down',
                        color: '#FF0000'
                    }, {
                        name: '2 Weeks Down',
                        color: '#820000'
                    }, {
                        name: '3 Weeks Down',
                        color: '#550000'
                    }]
                },

                plotOptions: {
                    sankey: {
                        allowPointSelect: true,
                        colorByPoint: true,
                        cursor: 'pointer',
                        dataLabels: {
                            enabled: true,
                            format: '<b>{point.to}</b><br>{point.weight:.1f} %',
                            //distance: -50,
                            filter: {
                                property: 'weight',
                                operator: '>',
                                value: 0
                            }
                        }
                    }

                },
                series: [{
                    keys: ['color', 'from', 'to', 'weight', 'value', 'listingId', 'type', 'trend'],
                    data: correlationValues,
                    type: 'sankey',
                    name: 'Sankey',
                    valueDecimals: 2,
                    minLinkWidth: 5,
                    linkOpacity: 1,
                    nodePadding: 25,
                    dataLabels: {
                        allowOverlap: true,
                        nodeFormat: '',
                        style: {
                            color: 'black',
                            textOutline: 'none'
                        },
                        format: "{point.to} {point.weight:.1f}%",

                    },
                    tooltip: {
                        valueDecimals: 2,
                        headerFormat: '',
                        pointFormat: '{point.fromNode.name} → {point.toNode.name} </br> <b>${point.value:,.f}</b>',
                    },
                    point: {
                        events: {
                            click: function () {
                                //Call C# Method
                                dotNetReference.invokeMethodAsync('GetTimeSeriesChartData', this.listingId, this.to, this.type, this.trend);

                            }
                        }
                    }
                }],

            }
            );
        }
    },
}