using Servisyo.Web.Models;

namespace Servisyo.Web.ViewModels;

/// The values posted by the "New Category" form and by the Edit dialog.
public class CategoryForm
{
    public int CategoryId { get; set; }        // only used when editing
    public string? Name { get; set; }
    public string? Description { get; set; }
}

/// The value posted by the Disable / Enable confirmation.
public class CategoryToggleForm
{
    public int CategoryId { get; set; }
    public bool Enable { get; set; }           // true = enable, false = disable
}

public class CategoryRowViewModel
{
    public int CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public int Providers { get; set; }         // approved providers that offer this category
    public int Bookings { get; set; }          // bookings that include a service from this category
    public string StatusText => IsActive ? "Active" : "Disabled";
    public string StatusTone => IsActive ? "completed" : "muted";   // green / grey
}

public class CategoriesPageViewModel
{
    public PortalShellViewModel Shell { get; set; } = new() { Role = "Admin" };
    public IReadOnlyList<CategoryRowViewModel> Rows { get; set; } = Array.Empty<CategoryRowViewModel>();
    public int ActiveCount { get; set; }
    public int DisabledCount { get; set; }

    // the "New Category" panel: normally closed; open again (with the typed text) when the server rejected it
    public bool ShowAddForm { get; set; }
    public CategoryForm NewCategory { get; set; } = new();
    public string? AddFormError { get; set; }

    public string? FlashMessage { get; set; }
    public bool FlashIsError { get; set; }
}
