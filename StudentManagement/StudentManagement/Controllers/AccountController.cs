using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using StudentManagement.Models;
using StudentManagement.ViewModels;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace StudentManagement.Controllers
{
    [AllowAnonymous]
    public class AccountController : Controller
    {
        private UserManager<ApplicationUser> userManager;
        private SignInManager<ApplicationUser> signInManager;
        private readonly ILogger logger;

        public AccountController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager,
            ILogger<AccountController> logger)
        {
            this.userManager = userManager;
            this.signInManager = signInManager;
            this.logger = logger;
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (ModelState.IsValid)
            {
                //将数据从RegisterViewModel赋值到ApplicationUser
                var user = new ApplicationUser
                {
                    UserName = model.Email,
                    Email = model.Email,
                    City = model.City
                };
                //将用户数据存储在AspNetUsers数据库表中
                var result = await userManager.CreateAsync(user, model.Password);

                //如果成功创建用户，则使用登录服务登录用户信息
                //并重定向到homecontroller的索引操作
                if (result.Succeeded)
                {
                    //生成电子邮件确认令牌
                    var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
                    //生成电子邮件的确认链接
                    var confirmationLink = Url.Action("ConfirmEmail", "Account",
                    new { userId = user.Id, token = token }, Request.Scheme);
                    //需要注入ILogger<AccountController> _logger;服务，记录生成的URL链接
                    logger.Log(LogLevel.Warning, confirmationLink);

                    //如果用户已登录并属于Admin角色。
                    //那么就是Admin正在创建新用户。
                    //所以重定向Admin用户到ListRoles的视图列表

                    if (signInManager.IsSignedIn(User) && User.IsInRole("Admin"))
                    {
                        return RedirectToAction("ListUsers", "Admin");
                    }

                    ViewBag.ErrorTitle = "注册成功";
                    ViewBag.ErrorMessage = $"在你登入系统前,我们已经给您发了一份邮件，需要您先进行邮件验证，点击确认链接即可完成。";
                    return View("Error");
                }

                //如果有任何错误，将它们添加到ModelState对象中
                //将由验证摘要标记助手显示到视图中
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            return View(model);
        }

        #region 登录功能

        [HttpGet]
        public async Task<IActionResult> Login(string returnUrl)
        {
            LoginViewModel model = new LoginViewModel
            {
                ReturnUrl = returnUrl,
                ExternalLogins =
                  (await signInManager.GetExternalAuthenticationSchemesAsync()).ToList()
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model, string returnUrl)
        {
            model.ExternalLogins =
    (await signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (ModelState.IsValid)
            {
                var user = await userManager.FindByEmailAsync(model.Email);

                if (user != null && !user.EmailConfirmed &&
                           (await userManager.CheckPasswordAsync(user, model.Password)))
                {
                    ModelState.AddModelError(string.Empty, "你的邮箱还没有通过验证，请前往验证。");
                    return View(model);
                }

                var result = await signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, false);

                if (result.Succeeded)
                {
                    if (!string.IsNullOrEmpty(returnUrl))
                    {
                        //防止开放式重定向攻击
                        if (Url.IsLocalUrl(returnUrl))
                        {
                            return Redirect(returnUrl);
                        }
                        else
                        {
                            //   ModelState.AddModelError(string.Empty, "防止开放式重定向攻击");
                            return RedirectToAction("Index", "home");
                        }
                    }
                    else
                    {
                        return RedirectToAction("Index", "home");
                    }
                }
                ModelState.AddModelError(string.Empty, "登录失败，请重试");
            }
            return View(model);
        }

        #endregion 登录功能

        #region 扩展登录

        [HttpPost]
        public IActionResult ExternalLogin(string provider, string returnUrl)
        {
            var redirectUrl = Url.Action("ExternalLoginCallback", "Account",
                         new { ReturnUrl = returnUrl });
            var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            return new ChallengeResult(provider, properties);
        }

        public async Task<IActionResult>
            ExternalLoginCallback(string returnUrl = null, string remoteError = null)
        {
            returnUrl = returnUrl ?? Url.Content("~/");

            LoginViewModel loginViewModel = new LoginViewModel
            {
                ReturnUrl = returnUrl,
                ExternalLogins =
                        (await signInManager.GetExternalAuthenticationSchemesAsync()).ToList()
            };

            if (remoteError != null)
            {
                ModelState
                    .AddModelError(string.Empty, $"外部提供程序错误: {remoteError}");

                return View("Login", loginViewModel);
            }

            // 从外部登录提供者,即微软账户体系中，获取关于用户的登录信息。
            var info = await signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                ModelState
                    .AddModelError(string.Empty, "加载外部登录信息出错。");

                return View("Login", loginViewModel);
            }

            // 获取邮箱地址
            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            ApplicationUser user = null;



            if (email != null)
            {
                // 通过邮箱地址去查询用户是否已存在
                user = await userManager.FindByEmailAsync(email);

                // 如果电子邮件没有被确认，返回登录视图与验证错误
                if (user != null && !user.EmailConfirmed)
                {
                    ModelState.AddModelError(string.Empty, "您的电子邮箱还未进行验证。");

                    return View("Login", loginViewModel);
                }
            }
            //如果用户之前已经登录过了，会在AspNetUserLogins表有对应的记录，这个时候无需创建新的记录，直接使用当前记录登录系统即可。
            var signInResult = await signInManager.ExternalLoginSignInAsync(info.LoginProvider,
                info.ProviderKey, isPersistent: false, bypassTwoFactor: true);

            if (signInResult.Succeeded)
            {
                return LocalRedirect(returnUrl);
            }

            //如果AspNetUserLogins表中没有记录，则代表用户没有一个本地帐户，这个时候我们就需要创建一个记录了。
            else
            {
                if (email != null)
                {
                    if (user == null)
                    {
                        user = new ApplicationUser
                        {
                            UserName = info.Principal.FindFirstValue(ClaimTypes.Email),
                            Email = info.Principal.FindFirstValue(ClaimTypes.Email)
                        };
                        //如果不存在，则创建一个用户，但是这个用户没有密码。
                        await userManager.CreateAsync(user);


                        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);

                        var confirmationLink = Url.Action("ConfirmEmail", "Account", new { userId = user.Id, token = token }, Request.Scheme);


                      logger.Log(LogLevel.Warning, confirmationLink);
                        ViewBag.ErrorTitle = "注册成功";
                        ViewBag.ErrorMessage = $"在你登入系统前,我们已经给您发了一份邮件，需要您先进行邮件验证，点击确认链接即可完成。";
                        return View("Error");
                    }

                    // 在AspNetUserLogins表中,添加一行用户数据，然后将当前用户登录到系统中
                    await userManager.AddLoginAsync(user, info);
                    await signInManager.SignInAsync(user, isPersistent: false);

                    return LocalRedirect(returnUrl);
                }
                // 如果我们获取不到电子邮件地址，我们需要将请求重定向到错误视图中。
                ViewBag.ErrorTitle = $"我们无法从提供商:{info.LoginProvider}中解析到您的邮件地址 ";
                ViewBag.ErrorMessage = "请通过联系 ltm@ddxc.org 寻求技术支持。";

                return View("Error");
            }
        }

        #endregion 扩展登录



        #region 确认邮箱

        [HttpGet]
        public async Task<IActionResult> ConfirmEmail(string userId, string token)
        {
            if (userId == null || token == null)
            {
                return RedirectToAction("index", "home");
            }
            var user = await userManager.FindByIdAsync(userId);

            if (user == null)
            {
                ViewBag.ErrorMessage = $"当前{userId}无效";
                return View("NotFound");
            }
            var result = await userManager.ConfirmEmailAsync(user, token);

            if (result.Succeeded)
            {
                return View();
            }

            ViewBag.ErrorTitle = "您的电子邮箱还未进行验证";
            return View("Error");
        }


        #endregion


        #region 激活邮箱

        [HttpGet]
        public IActionResult ActivateUserEmail()
        {


            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ActivateUserEmail(EmailAddressViewModel model)
        {

            if (ModelState.IsValid)
            {

             var user=   await userManager.FindByEmailAsync(model.Email);

                if (user!=null)
                {
                    //当前已经存在老用户
                    //生成电子令牌
                    //以及电子令牌确认URL


                    if (! await userManager.IsEmailConfirmedAsync(user))
                    {
                        //生成电子邮件确认令牌
                        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);

                        //生成电子邮件的确认链接
                        var confirmationLink = Url.Action("ConfirmEmail", "Account",
                        new { userId = user.Id, token = token }, Request.Scheme);

                        logger.Log(LogLevel.Warning, confirmationLink);
                        ViewBag.Message = "如果你在我们系统有注册账户，我们已经发了邮件到您的邮箱中，请前往邮箱激活您的用户。";
                        //重定向用户到忘记密码确认视图
                        return View("ActivateUserEmailConfirmation", ViewBag.Message);
                    }

                


                }


                ViewBag.Message = "请确认邮箱是否存在异常，现在我们无法给您发送激活链接。";
                // 为了避免帐户枚举和暴力攻击，所以不进行用户不存在或邮箱未验证的提示
                return View("ActivateUserEmailConfirmation", ViewBag.Message);
            }


            return View();
        }


        #endregion
        #region 找回密码& 重置密码

        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }


        [HttpPost]
        public async Task<IActionResult> ForgotPassword(EmailAddressViewModel model)
        {

            if (ModelState.IsValid)
            {
                // 通过邮件地址查询用户地址
                var user = await userManager.FindByEmailAsync(model.Email);
                // 如果找到了用户并且确认了电子邮件
                if (user != null && await userManager.IsEmailConfirmedAsync(user)) {

                    //生成重置密码令牌
                    var token = await userManager.GeneratePasswordResetTokenAsync(user);

                    // 生成密码重置链接
                    var passwordResetLink = Url.Action("ResetPassword", "Account",
                            new { email = model.Email, token = token }, Request.Scheme);

                    // 将密码重置链接记录到文件中
                    logger.Log(LogLevel.Warning, passwordResetLink);
                    //重定向用户到忘记密码确认视图
                    return View("ForgotPasswordConfirmation");
                }

                // 为了避免帐户穷举和暴力攻击，所以不进行用户不存在或邮箱未验证的提示
                return View("ForgotPasswordConfirmation");
            }

            return View(model);
        }


        // 电子邮箱 重置密码的token  新密码 确认密码


        [HttpGet]
        public IActionResult ResetPassword(string token, string email)
        {

            //如果密码的token或者邮箱地址为空，用户有可能在试图篡改密码重置的URL

            if (token==null||email==null)
            {

                ModelState.AddModelError("", "当前的密码重置令牌无效");

            }

            return View();


        }

        [HttpPost]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (ModelState.IsValid)
            {
                // 通过电子邮件查找用户
                var user = await userManager.FindByEmailAsync(model.Email);

                if (user != null)
                {
                    //重置用户密码

                 var result=   await userManager.ResetPasswordAsync(user,model.Token,model.Password);

                    if (result.Succeeded)
                    {
                        return View("ResetPasswordConfirmation");

                    }

                    //告诉它验证不通过的错误信息
                    foreach (var error in result.Errors)
                    {
                        ModelState.AddModelError("", error.Description);
                    }
                    return View(model);
                }

                // 为了避免帐户穷举和暴力攻击，不要提示用户不存在
                return View("ResetPasswordConfirmation");
            }
            // 如果模型验证未通过，则显示验证错误
            return View(model);
        }



        #endregion

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await signInManager.SignOutAsync();

            return RedirectToAction("index", "home");
        }

        [AcceptVerbs("Get", "Post")]
        public async Task<IActionResult> IsEmailInUse(string email)
        {
            var user = await userManager.FindByEmailAsync(email);

            if (user == null)
            {
                return Json(true);
            }
            else
            {
                return Json($"邮箱：{email}已经被注册使用了。");
            }
        }
    }
}