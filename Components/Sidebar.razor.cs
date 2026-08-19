using Microsoft.AspNetCore.Components;

namespace Hypen.Web.Components
{
    public partial class Sidebar
    {
        private bool isExpanded = true;

        [Parameter]
        public EventCallback<bool> OnToggle { get; set; }

        private async Task ToggleSidebar()
        {
            isExpanded = !isExpanded;

            if (OnToggle.HasDelegate)
            {
                await OnToggle.InvokeAsync(isExpanded);
            }
        }
    }
}
