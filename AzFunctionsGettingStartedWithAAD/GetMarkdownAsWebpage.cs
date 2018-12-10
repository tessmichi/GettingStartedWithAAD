using Markdig;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace GettingStartedAAD
{
    public static class GetMarkdownAsWebpage
    {
        [FunctionName("GetMarkdownAsWebpage")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request.");

            // Get request body
            var recipe = await req.Content.ReadAsStringAsync();

            // Get markdown recipe from reuqest body
            //string recipe = data?.markdown;
            if (recipe == null || recipe.Length == 0)
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, "Please pasmarkdown in query body");
            }

            // from richard
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
            var html = Markdig.Markdown.ToHtml(recipe, pipeline);

            //fix table styling
            html = html.Replace("<table", "<table class='table table-striped'");

            return req.CreateResponse(HttpStatusCode.OK, html);
        }
    }
}
