using Microsoft.AspNetCore.Components;

namespace Hypen.Web.Components
{
    public partial class Sidebar
    {
        private bool isExpanded = true;

        [Parameter]
        public EventCallback<bool> OnToggle { get; set; }

        [Inject]
        protected NavigationManager Navigation { get; set; } = default!;

        private async Task ToggleSidebar()
        {
            isExpanded = !isExpanded;

            if (OnToggle.HasDelegate)
            {
                await OnToggle.InvokeAsync(isExpanded);
            }
        }

        protected void NavigateToLibrary()
        {
            Navigation.NavigateTo("/library");
        }

        protected void NavigateToSync()
        {
            Navigation.NavigateTo("/extraction");
        }

        protected void NavigateToStaging()
        {
            Navigation.NavigateTo("/staging");
        }

        protected void NavigateToTools()
        {
            Navigation.NavigateTo("/tools");
        }

        protected void NavigateToSetting()
        {
            Navigation.NavigateTo("/setting");
        }
    }
}
