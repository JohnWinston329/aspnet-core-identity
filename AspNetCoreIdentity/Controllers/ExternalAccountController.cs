﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using AspNetCoreIdentity.Infrastructure;
using AspNetCoreIdentity.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AspNetCoreIdentity.Controllers
{
    [Route("[controller]/[action]")]
    public class ExternalAccountController : Controller
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IEmailSender _emailSender;

        public ExternalAccountController(SignInManager<IdentityUser> signInManager,
            UserManager<IdentityUser> userManager, IEmailSender emailSender)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _emailSender = emailSender;
        }

        public IActionResult Index()
        {
            return View("../Home/Index");
        }

        [HttpGet]
        public IActionResult Login(string provider, string returnUrl = null)
        {
            var redirectUrl = "/ExternalAccount/Callback";
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            return new ChallengeResult(provider, properties);
        }

        [HttpGet]
        public async Task<IActionResult> Callback(string returnUrl = null, string remoteError = null)
        {
            returnUrl = returnUrl ?? Url.Content("~/");
            if (remoteError != null)
            {
                //ErrorMessage = $"Error from external provider: {remoteError}";
                return RedirectToPage("./", new { ReturnUrl = returnUrl });
            }

            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                return RedirectToPage("./", new { ReturnUrl = returnUrl });
            }

            // Sign in the user with this external login provider if the user already has a login.
            var result = await _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey,
                isPersistent: false, bypassTwoFactor: true);
            if (result.Succeeded)
            {
                return LocalRedirect(returnUrl);
            }


            // If the user does not have an account, create one.
            var userEmail = info.Principal.FindFirstValue(ClaimTypes.Email);

            var user = new IdentityUser { Id = Guid.NewGuid().ToString(), UserName = userEmail, Email = userEmail };

            var userDb = await _userManager.FindByEmailAsync(userEmail);

            if (userDb != null)
            {
                var emailConfirmed = await _userManager.IsEmailConfirmedAsync(userDb);

                if (!result.IsNotAllowed)
                {
                    var newLoginResult = await _userManager.AddLoginAsync(userDb, info);
                    if (newLoginResult.Succeeded)
                    {
                        // Check Email Confirmation
                        if (emailConfirmed)
                        {
                            await _signInManager.SignInAsync(userDb, isPersistent: false);
                            return LocalRedirect(
                                $"{returnUrl}?message={info.ProviderDisplayName} has been added successfully");
                        }

                        return LocalRedirect(
                            $"{returnUrl}?message={info.ProviderDisplayName} has been added but email confirmation is pending");
                    }
                }
                else
                {
                    return LocalRedirect(
                        $"{returnUrl}?message=Email ({user.Email}) confirmation is pending&type=danger");
                }
            }

            return LocalRedirect($"/register?associate={userEmail}&loginProvider={info.LoginProvider}&providerDisplayName={info.ProviderDisplayName}&providerKey={info.ProviderKey}");

        }

        [HttpGet]
        public async Task<IActionResult> Providers()
        {
            var schemes = await _signInManager.GetExternalAuthenticationSchemesAsync();

            return Ok(schemes.Select(s => s.DisplayName).ToList());
        }

        [HttpPost]
        [Route("/api/externalaccount/associate")]
        public async Task<ResultVM> Associate([FromBody] AssociateViewModel associate)
        {
            // Create a new account..
            if (!associate.associateExistingAccount)
            {
                var user = new IdentityUser
                { Id = Guid.NewGuid().ToString(), UserName = associate.Username, Email = associate.OriginalEmail };

                var createUserResult = await _userManager.CreateAsync(user);
                if (createUserResult.Succeeded)
                {
                    // Add the Trial claim..
                    Claim trialClaim = new Claim("Trial", DateTime.Now.ToString());
                    await _userManager.AddClaimAsync(user, trialClaim);

                    createUserResult =
                        await _userManager.AddLoginAsync(user,
                            new ExternalLoginInfo(null, associate.LoginProvider, associate.ProviderKey,
                                associate.ProviderDisplayName));
                    if (createUserResult.Succeeded)
                    {
                        await _signInManager.SignInAsync(user, isPersistent: false);

                        // No need to send a confirmation email here..
                        var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                        await _userManager.ConfirmEmailAsync(user, code);
                        return new ResultVM
                        {
                            Status = Status.Success,
                            Message = $"{user.UserName} has been created successfully",
                            Data = new { username = user.UserName }
                        };
                    }
                }

                var resultErrors = createUserResult.Errors.Select(e => "<li>" + e.Description + "</li>");
                return new ResultVM
                {
                    Status = Status.Error,
                    Message = "Invalid data",
                    Data = string.Join("", resultErrors)
                };
            }

            var userDb = await _userManager.FindByEmailAsync(associate.AssociateEmail);

            if (userDb != null)
            {
                if (!userDb.EmailConfirmed)
                {
                    return new ResultVM
                    {
                        Status = Status.Error,
                        Message = "Invalid data",
                        Data = $"<li>Associated account (<i>{associate.AssociateEmail}</i>) hasn't been confirmed yet.</li><li>Confirm the account and try again</li>"
                    };
                }

                var token = await _userManager.GenerateEmailConfirmationTokenAsync(userDb);

                var callbackUrl = Url.Action("ConfirmExternalProvider", "Account",
                    values: new
                    {
                        userId = userDb.Id,
                        code = token,
                        loginProvider = associate.LoginProvider,
                        providerDisplayName = associate.LoginProvider,
                        providerKey = associate.ProviderKey
                    },
                    protocol: Request.Scheme);

                await _emailSender.SendEmailAsync(userDb.Email, $"Confirm {associate.ProviderDisplayName} external login",
                    $"Please confirm association of your {associate.ProviderDisplayName} account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");

                return new ResultVM
                {
                    Status = Status.Success,
                    Message = "External account association is pending. Please check your email"
                };
            }

            return new ResultVM
            {
                Status = Status.Error,
                Message = "Invalid data",
                Data = $"<li>User with email {associate.AssociateEmail} not found</li>"
            };
        }
    }
}
