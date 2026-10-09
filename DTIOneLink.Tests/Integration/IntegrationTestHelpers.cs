using System.Text.RegularExpressions;
using DTIOneLink.Models;
using Microsoft.AspNetCore.Identity;

namespace DTIOneLink.Tests.Integration;

public static class IntegrationTestHelpers
{
    private static readonly Regex AntiforgeryTokenPattern = new(
        @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""", RegexOptions.Compiled);

    // The antiforgery cookie itself is carried automatically by
    // HttpClient's cookie container (WebApplicationFactoryClientOptions
    // defaults HandleCookies to true) - this just pulls the matching
    // hidden form field's token out of a GET response's HTML.
    public static async Task<string> ExtractAntiforgeryTokenAsync(HttpResponseMessage response)
    {
        var html = await response.Content.ReadAsStringAsync();
        var match = AntiforgeryTokenPattern.Match(html);
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"No __RequestVerificationToken found in response from {response.RequestMessage?.RequestUri}.");
        }
        return match.Groups[1].Value;
    }

    // Program.cs maps BOTH "" and "Account" to {controller=Account,
    // action=Login}, so link generation for RedirectToAction("Login",
    // "Account") can legitimately come out as "/" rather than
    // "/Account/Login" - assert on the actual routing contract, not one
    // specific spelling of it.
    public static void AssertRedirectsToLogin(HttpResponseMessage response)
    {
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.True(location == "/" || location.StartsWith("/Account"),
            $"Expected a redirect to the login page, got \"{location}\".");
    }

    public static User NewActiveUser(string role, string department, string password = "Correct-Horse-1",
        string username = "test.user@dti.gov.ph", bool mustChangePassword = false)
    {
        var user = new User
        {
            Username = username,
            Email = username,
            FullName = "Test User",
            Role = role,
            Department = department,
            IsActive = true,
            EmailConfirmed = true,
            MustChangePassword = mustChangePassword,
            SecurityStamp = Guid.NewGuid().ToString("N"),
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
        return user;
    }
}
