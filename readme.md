# Background email sender sample
This application is a clone of the homonymous application of the one developed by BrightSoul available at this link: https://github.com/BrightSoul/background-email-sender.

## Dependencies
This project uses the `MailKit` package to deliver emails. The Hosted Service implements the `IEmailSender` interface from the `Microsoft.AspNetCore.Identity.UI` but you can make it implement some other similar interface if you don't want to depend upon ASP.NET Core Identity.

## Thanks
I thank Moreno G. for providing me with the appropriate information so that I could finish the implementation and make it usable for the production environment.

## Getting started
Edit the [appsettings.json](appsettings.json) file with your SMTP server data. Then, just run the application by typing `dotnet run`. The .NET SDK 6.x (or greater) must be installed in your system. Fill in the form and hit the Send button.

![home.png](home.png)
