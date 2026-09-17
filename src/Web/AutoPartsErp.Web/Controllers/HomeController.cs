using Microsoft.AspNetCore.Mvc;

namespace AutoPartsErp.Web.Controllers;

/// <summary>Where a signed-in person lands.</summary>
public sealed class HomeController : Controller
{
    /// <summary>The front page.</summary>
    [HttpGet]
    public IActionResult Index() => View();
}
