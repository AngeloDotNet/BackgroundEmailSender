namespace BackgroundEmailSenderSample.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> SendMailAsync(ContactInputModel inputModel, [FromServices] IEmailSender emailSender)
    {
        await emailSender.SendEmailAsync(inputModel.Email, "Request from our website", inputModel.ToHtmlMessage());
        return RedirectToAction(nameof(ThankYou));
    }

    public IActionResult ThankYou()
    {
        return View();
    }
}