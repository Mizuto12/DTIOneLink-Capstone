using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace DTIOneLink.Tests.TestDoubles;

// TempData needs *some* ITempDataProvider to exist; the controllers under
// test only write to TempData and redirect, they never read it back within
// the same test, so a no-op store is enough.
public class FakeTempDataProvider : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}
