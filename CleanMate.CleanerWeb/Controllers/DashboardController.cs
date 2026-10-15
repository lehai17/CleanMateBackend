using CleanMate.CleanerWeb.Services;
using Microsoft.AspNetCore.Mvc;

namespace CleanMate.CleanerWeb.Controllers;

[CleanerSession]
public class DashboardController(CleanerApiClient api) : Controller
{
    public async Task<IActionResult> Index() => View(await api.GetProfileAsync());
}
