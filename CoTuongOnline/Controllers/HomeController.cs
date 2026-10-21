using Microsoft.AspNetCore.Mvc;
namespace CoTuongOnline.Controllers;
public sealed class HomeController : Controller
{
    [HttpGet("/"), HttpGet("/account/login"), HttpGet("/account/register"), HttpGet("/rooms/{code}"), HttpGet("/rooms/join/{code}")]
    public IActionResult Index() => View();
}
