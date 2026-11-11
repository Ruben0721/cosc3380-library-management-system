using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Project.Auth;
using Project.Models;
using Project.Pages.Staff;

int checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    checks++;
}
ClaimsPrincipal Actor(string role, bool authenticated = true) => new(new ClaimsIdentity(
    new[] { new Claim(ClaimTypes.Role, role) }, authenticated ? "test" : null));

foreach (var role in new[] { "Student", "Faculty", "Staff", "Admin", "Unknown" })
foreach (var target in new[] { "Student", "Faculty", "Staff", "Admin", "Unknown" })
{
    bool expected = (role == "Staff" && (target == "Student" || target == "Faculty")) ||
                    (role == "Admin" && (target == "Student" || target == "Faculty" || target == "Staff"));
    Check(AccountAccessRules.CanResetPassword(Actor(role), target) == expected,
        $"Reset permission: {role} -> {target}");
    Check(!AccountAccessRules.CanResetPassword(Actor(role, false), target), "Anonymous reset denied");
}
Check(!AccountAccessRules.CanManageAccountRole(Actor("Staff"), "Staff", "Admin"), "Forged promotion denied");
Check(!AccountAccessRules.CanManageAccountRole(Actor("Staff"), "Admin", "Staff"), "Admin demotion denied");
Check(!AccountAccessRules.CanManageAccountRole(Actor("Staff"), "Admin", "Admin"), "Admin editing denied");
Check(!AccountAccessRules.CanManageAccountRole(Actor("Staff"), null, "Admin"), "Admin creation denied");
Check(AccountAccessRules.CanManageAccountRole(Actor("Admin"), "Staff", "Admin"), "Admin promotion allowed");
Check(AccountAccessRules.CanManageAccountRole(Actor("Staff"), "Faculty", "Faculty"), "Faculty editing allowed");

var account = new UserAccount();
Check(account.SessionVersion == 1, "New accounts start at version 1");
foreach (var value in new[] { "", "bad", "0", "2" })
{
    var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("session_version", value) }));
    Check(!AccountAccessRules.HasCurrentSessionVersion(principal, account), "Invalid or stale session rejected");
}
Check(!AccountAccessRules.HasCurrentSessionVersion(Actor("Faculty"), account), "Pre-deployment cookie rejected");
var cookie = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("session_version", "1") }));
Check(AccountAccessRules.HasCurrentSessionVersion(cookie, account), "Current session accepted");
account.SessionVersion++;
Check(!AccountAccessRules.HasCurrentSessionVersion(cookie, account), "Reset invalidates previous session");

bool Valid(string password, string confirmation)
{
    var input = new ResetPasswordModel.ResetInput { NewPassword = password, ConfirmPassword = confirmation };
    return Validator.TryValidateObject(input, new ValidationContext(input), new List<ValidationResult>(), true);
}
Check(!Valid("", ""), "Empty password rejected");
Check(!Valid("short", "short"), "Short password rejected");
Check(!Valid(new string('a', 129), new string('a', 129)), "Long password rejected");
Check(!Valid("TwelveChars12", "Different123"), "Mismatch rejected");
Check(Valid("TwelveChars12", "TwelveChars12"), "Valid password accepted");
bool ValidChange(string current, string password, string confirmation)
{
    var input = new Project.Pages.Account.ChangePasswordModel.ChangeInput
    {
        CurrentPassword = current,
        NewPassword = password,
        ConfirmPassword = confirmation
    };
    return Validator.TryValidateObject(input, new ValidationContext(input), new List<ValidationResult>(), true);
}
Check(!ValidChange("", "TwelveChars12", "TwelveChars12"), "Current password required");
Check(!ValidChange("OldPassword12", "short", "short"), "Short new password rejected");
Check(!ValidChange("OldPassword12", new string('a', 129), new string('a', 129)), "Long new password rejected");
Check(!ValidChange("OldPassword12", "TwelveChars12", "Different123"), "Change confirmation mismatch rejected");
Check(!ValidChange("OldPassword12", "OldPassword12", "OldPassword12"), "Reusing current password rejected");
Check(ValidChange("OldPassword12", "TwelveChars12", "TwelveChars12"), "Valid change accepted");
Check(Attribute.IsDefined(typeof(Project.Pages.Account.ChangePasswordModel),
    typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute)), "Change password requires authentication");


// Employee account management is reserved for administrators.
Check(!AccountAccessRules.CanManageAccountRole(Actor("Staff"), "Staff", "Staff"), "Staff editing another staff account denied");
Check(!AccountAccessRules.CanManageAccountRole(Actor("Staff"), null, "Staff"), "Staff account creation denied");
Check(!AccountAccessRules.CanManageAccountRole(Actor("Staff"), "Staff", "Faculty"), "Staff cannot bypass protection by changing the target role");
Check(AccountAccessRules.CanManageAccountRole(Actor("Admin"), "Staff", "Staff"), "Admin editing staff allowed");
Console.WriteLine($"Passed {checks} authentication and password validation checks. No database accessed.");
