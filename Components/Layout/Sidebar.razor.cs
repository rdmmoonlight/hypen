using Microsoft.AspNetCore.Components;

namespace Hypen.Web.Components
{
    public partial class Sidebar
    {
        private bool isExpanded = true;
        private bool isToolsSubmenuOpen = false;

        [Parameter]
        public EventCallback<bool> OnToggle { get; set; }

        [Inject]
        protected NavigationManager Navigation { get; set; } = default!;

        private async Task ToggleSidebar()
        {
            isExpanded = !isExpanded;

            if (!isExpanded)
            {
                isToolsSubmenuOpen = false; // Otomatis tutup submenu jika sidebar di-collapse
            }

            if (OnToggle.HasDelegate)
            {
                await OnToggle.InvokeAsync(isExpanded);
            }
        }

        private async Task ToggleToolsSubmenu()
        {
            if (!isExpanded)
            {
                isExpanded = true;
                if (OnToggle.HasDelegate)
                {
                    await OnToggle.InvokeAsync(isExpanded);
                }
            }

            isToolsSubmenuOpen = !isToolsSubmenuOpen;
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

        protected void NavigateToDuplicateDetector()
        {
            Navigation.NavigateTo("/tools/dedup");
        }

        protected void NavigateToLocalSync()
        {
            Navigation.NavigateTo("/tools/localsync");
        }

        protected void NavigateToGDriveManagement()
        {
            Navigation.NavigateTo("/tools/drivedetector");
        }

        protected void NavigateToSetting()
        {
            Navigation.NavigateTo("/settings");
        }
    }
}
