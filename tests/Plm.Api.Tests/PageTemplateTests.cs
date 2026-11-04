using Jellyfin.Plugin.Plm;
using Xunit;

namespace Plm.Api.Tests;

public class PageTemplateTests
{
 [Fact] public void LoaderIsScopedAndIdempotentAndUnknownTemplatesArePreserved()
 {
  const string template="<script>setTimeout(function(){ let container=document.querySelector('.userPreferencesPage:not(.hide)'); }, 100);</script>";
  var transformed=PluginPageTemplate.Transform(new(){Contents=template});
  Assert.Contains("document.currentScript?.closest",transformed);
  Assert.Contains("container.isConnected",transformed);
  Assert.Contains("ApiClient.getCurrentUserId()!==owner",transformed);
  Assert.DoesNotContain("setTimeout",transformed);
  Assert.Equal(transformed,PluginPageTemplate.Transform(new(){Contents=transformed}));
  Assert.Equal("<html>future</html>",PluginPageTemplate.Transform(new(){Contents="<html>future</html>"}));
 }
}
