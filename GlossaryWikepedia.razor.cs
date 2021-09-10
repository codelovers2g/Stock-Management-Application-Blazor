using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.JSInterop;

namespace Demo.Pages
{
    public partial class GlossaryWikepedia : ComponentBase
    {
        [Parameter]
        public WikipediaApiModel Htmldatas { get; set; } = new WikipediaApiModel();
        [Parameter]
        public bool IsPortfolio { get; set; }
        [CascadingParameter] 
        private Task<AuthenticationState> AuthenticationState { get; set; }
        [Inject]
        public IJSRuntime JSRuntime { get; set; }
        [Inject]
        public IGlossaryRepository glossaryRepository { get; set; }
        [Inject]
        public AuthenticationStateProvider AuthenticationStateProvider { get; set; }
        [Inject]
        public IGlossaryWiikepedia IGlossaryWiikepedia { get; set; }
        [Inject]
        public IGlossaryWikipediaImages GlossaryWikipediaImages { get; set; }
        [Inject]
        public NavigationManager NavigationManager { get; set; }
      
        public WikipediaApiModel SetHtmldata { get; set; } = new WikipediaApiModel();
        public WikipediaImageModel ImageData { get; set; } = new WikipediaImageModel();

        public bool isLoadingData { get; set; }
        public bool isStartingText { get; set; } = true;
        public string ImageUrL { get; set; } = string.Empty;
        public string ImageDesceription { get; set; } = string.Empty;

        public override async Task SetParametersAsync(ParameterView parameters)
        {

            if (Htmldatas.Text != null)
            {
                SetHtmldata = Htmldatas;
                isStartingText = false;
                StateHasChanged();
                isLoadingData = false;

            }
            StateHasChanged();
            await base.SetParametersAsync(parameters);
        }


        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthenticationState;
            var user = authState.User;
            if (user.Identity.IsAuthenticated)
            {
                StateHasChanged();
                if (Htmldatas.Text != null)
                {
                    isStartingText = false;
                    isLoadingData = true;
                    StateHasChanged();
                    SetHtmldata = Htmldatas;

                    isLoadingData = false;
                }
                StateHasChanged();
            }
            else
            {
                NavigationManager.NavigateTo($"authentication/login");
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            await JSRuntime.InvokeVoidAsync("ExpandalbleLink");
            await JSRuntime.InvokeVoidAsync("setHref");
            await JSRuntime.InvokeVoidAsync("closePopUp");
            await JSRuntime.InvokeVoidAsync("WikiToTop");
            await JSRuntime.InvokeVoidAsync("AddBlankToHref");
            await JSRuntime.InvokeVoidAsync("ChangineURlofImage");
            var dotNetReferenceWikipedia = DotNetObjectReference.Create(this);
            await JSRuntime.InvokeVoidAsync(
            "setDotNetReferenceWikipedia",
            new object[] {
            dotNetReferenceWikipedia
            });
        }
        /// <summary>
        /// This will call from Js function
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        [JSInvokable("GetImageUrlDescription")]
        public async Task GetImageUrlDescription(string url)
        {
            var ImageUrlDescription = await GlossaryWikipediaImages.GetGlossaryWikepediaImageDesription(url);
            if (ImageUrlDescription.url != null)
            {
                ImageUrL = ImageUrlDescription.url;
                ImageDesceription = ImageUrlDescription.ImageDescription;
                await JSRuntime.InvokeVoidAsync("Open", ImageUrL, ImageDesceription);
            }
        }
        /// <summary>
        /// close the popup
        /// </summary>
        /// <returns></returns>
        public async Task ClosePopUp()
        {
            await JSRuntime.InvokeVoidAsync("closePopUp");
        }
    }
}
