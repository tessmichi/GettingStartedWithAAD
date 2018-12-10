using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GettingStartedAAD
{
    /*
     * Markdown requirements:
     *  Must have at least 1 #
     *  No higher than subheader 5 can be the start of a recognized section
     *  Headers adhere to markdown standerds in that they are of the form "\n# " with any amount of #s
     *  
     * Data requirements:
     *  Must contain header1, 2, 3, 4 selections. if none, empty string
     **/

    public static class GetRecipe
    {
        [FunctionName("GetRecipe")]
        public static async Task<HttpResponseMessage> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)]HttpRequestMessage req,
            [DocumentDB(
                databaseName: "existing_documentation",
                collectionName: "full_markdowns",
                ConnectionStringSetting = "CosmosDB_Endpoint")] DocumentClient client,
            TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request.");

            // parse query parameter
            var chosenSelectionsRaw = req.GetQueryNameValuePairs()
                .FirstOrDefault(q => string.Compare(q.Key, "selections", true) == 0)
                .Value;
            var chosenSelections = chosenSelectionsRaw.Split(',');
            if (chosenSelections == null || chosenSelections.Count() == 0)
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, "Please pass selections on the query string");
            }

            // Start new document with a doc-specific title
            StringBuilder markdown = new StringBuilder($"# {chosenSelectionsRaw}\n");

            // Get all stored document section records from the cosmos
            // TODO: actually filter by the tags, unsure why this is broken.
            var collectionUri = UriFactory.CreateDocumentCollectionUri("existing_documentation", "full_markdowns");
            log.Info($"Filtering markdown snippets by {chosenSelectionsRaw} using Uri: {collectionUri.ToString()}");
            var query = client.CreateDocumentQuery<MarkdownItem>(collectionUri)
                //.Where(p =>
                //    p.id == "0"
                //    //p.output_relevant_selections.All(s => chosenSelections.Contains(s))
                //)
                .OrderBy(d => d.output_level)
                .AsDocumentQuery();

            // Iterate over stored document sections received from cosmos
            while (query.HasMoreResults)
            {
                foreach (MarkdownItem result in await query.ExecuteNextAsync())
                {
                    // Filter out only records where all the record's tags are somewhere in the list of chosen tags
                    if (result.output_relevant_selections.All(s => chosenSelections.Contains(s)))
                    {
                        // Get the actual markdown string that this record points to
                        var section = await RetrieveMarkdown(result);

                        // Rename the section if it's one of the top level sections
                        if (result.output_topic != null && result.output_topic.Length > 0)
                        {
                            section = section.Substring(section.IndexOf("\n"));
                            section = $"## {result.output_topic}{section}";
                        }

                        // Add this section to the total list of markdown
                        markdown.Append(section);

                        log.Info($"Adding {result.output_relevant_selections.ToString()}");
                    }
                    else
                    {
                        log.Info($"Ignoring {result.output_relevant_selections.ToString()}");
                    }
                }
            }

            return req.CreateResponse(HttpStatusCode.OK, markdown.ToString());
        }

        public async static Task<string> RetrieveMarkdown(MarkdownItem markdownItem)
        {
            // Set up to make an HTTPGET request
            var client = new HttpClient();
            client.BaseAddress = new Uri(markdownItem.input_markdown_file);
            //client.DefaultRequestHeaders.Accept.Clear();

            // Get markdown as string from endpoint
            var response = await client.GetAsync(markdownItem.input_markdown_file);
            if (response.IsSuccessStatusCode)
            {
                var entire_markdown = await response.Content.ReadAsStringAsync();

                // TODO dynamically parse out all headers

                // parse out everything before the relevant header1, header2, header3, header4
                var relevant_subsections = ParseOutSubsection(entire_markdown, "#", markdownItem.input_section_path.header1);
                relevant_subsections = ParseOutSubsection(relevant_subsections, "##", markdownItem.input_section_path.header2);
                relevant_subsections = ParseOutSubsection(relevant_subsections, "###", markdownItem.input_section_path.header3);
                relevant_subsections = ParseOutSubsection(relevant_subsections, "####", markdownItem.input_section_path.header4);

                // parse out relative paths and replace
                relevant_subsections = FixRelativePaths(relevant_subsections, markdownItem.input_docs_file);

                // update depths of headers
                // TODO do this within the parsing out the sections
                relevant_subsections = UpdateDepths(
                    $"{relevant_subsections.Trim().Replace("\r\n", "\n").Replace("\r", "\n")}\n",
                    markdownItem.relative_top_level);

                return relevant_subsections;
            }

            return "Error loading this section";
        }

        // Finds and replaces all relative strings
        // TODO match more than just (.+/___); check with markup guidelines to see what else may look like this
        public static string FixRelativePaths(string input, string currentPath)
        {
            var markedAsAReference = $"{Regex.Escape("(")}{Regex.Escape(".")}+/.*{Regex.Escape(")")}"; // TODO also include (__
            //var markedAsAReference = $"{Regex.Escape("]")}{Regex.Escape("(")}{Regex.Escape(".")}*/?.*{Regex.Escape(")")}";

            foreach (Match currentfilename in Regex.Matches(input, markedAsAReference, RegexOptions.RightToLeft))
            {
                var indexToStartReplacementAt = currentfilename.Index;
                var replaceString = currentfilename.Value;

                // Get rid of the relative string
                input = input.Remove(indexToStartReplacementAt, replaceString.Length);

                // Add the new string
                input = input.Insert(
                    indexToStartReplacementAt,
                    $"({RelativeToFullPath(currentPath, replaceString.TrimStart('(').TrimEnd(')'))})");
            }

            return input;
        }

        // Concatenates a full path given a filename, and a reference from that file to another one deeper
        public static string RelativeToFullPath(string referenceFrom, string referenceTo)
        {
            referenceFrom = referenceFrom.Substring(0, referenceFrom.LastIndexOf('/'));

            // if you need to go shallower to get to most common folder, start removing them folder by folder
            while (referenceTo.StartsWith("../"))
            {
                referenceFrom = referenceFrom.Substring(0, referenceFrom.LastIndexOf('/'));
                referenceTo = referenceTo.Substring(referenceTo.IndexOf("../") + 3);
            }

            // concat the source folder path with the relative path
            return referenceFrom + "/" + referenceTo;
        }

        public static string UpdateDepths(string input, int goalShallowest)
        {
            // TODO this could be done in first iteration of foreach for more effective use of resources
            var currentShallowest = Regex.Match(input, $"^#+[^#]", RegexOptions.Multiline);
            if (!currentShallowest.Success)
            {
                return input;
            }

            var depthDifference = goalShallowest - currentShallowest.Value.Trim().Length;

            // Return if the top level header is at the right top level
            if (depthDifference == 0)
            {
                return input;
            }

            // Cycle every header and make necessary changes to its length
            foreach (Match currentHeader in Regex.Matches(input, $"^#+[^#]", RegexOptions.Multiline | RegexOptions.RightToLeft))
            {
                int amtLeadingWhitespace = currentHeader.Value.Length - currentHeader.Value.TrimStart().Length;

                for (int i = 0; i < Math.Abs(depthDifference); i++)
                {
                    if (depthDifference < 0)
                    {
                        input = input.Remove(currentHeader.Index + amtLeadingWhitespace, 1);
                    }
                    else
                    {
                        input = input.Insert(currentHeader.Index + amtLeadingWhitespace, "#");
                    }
                }
            }

            return input;
        }

        public static string ParseOutSubsection(string input, string parseOut, int? occurranceCount)
        {
            // TODO instead, never call this function in this scenario
            if (occurranceCount == null)
                return input;

            var amountDeep = parseOut.Length;
            var zeroOrOnePounds = "#?";
            var matchItOrShallower = "#";
            for (int i = 1; i < amountDeep; i++)
            {
                matchItOrShallower += zeroOrOnePounds;
            }

            // parse everything before the start
            for (int i = 0; i <= occurranceCount + 1; i++)
            {
                var firstInstance = Regex.Match(input, $"^{matchItOrShallower}[^#]", RegexOptions.Multiline);
                if (firstInstance.Success)
                {
                    input = input.Substring(firstInstance.Index + firstInstance.Value.Length);
                }
            }
            input = $"{parseOut} {input}";

            // parse everything after the next one
            var LastInstance = Regex.Match(input, $"^{matchItOrShallower}[^#]", RegexOptions.Multiline).NextMatch();
            if (LastInstance.Success)
            {
                input = input.Substring(0, LastInstance.Index);
            }

            return $"\n{input}";
        }
    }

    public class MarkdownItem
    {
        // _ represents items from the db, camel case represents items living only in c#

        public string id { get; set; }
        public string input_markdown_file { get; set; }
        public string input_docs_file { get; set; }
        public MarkdownSections input_section_path { get; set; }
        public MarkdownSections[] input_section_path_exclude { get; set; } // TODO implement ignoring these sections
        public string output_topic { get; set; }
        public double output_level { get; set; }
        public int relative_top_level { get; set; }
        public string[] output_relevant_selections { get; set; }

        public int sourceShallowest { get {
                return
                    input_section_path.header2 == null ? 1 :
                    input_section_path.header3 == null ? 2 :
                    input_section_path.header4 == null ? 3 :
                    4;
            } }
    }

    public class MarkdownSections
    {
        public int? header1 { get; set; }
        public int? header2 { get; set; }
        public int? header3 { get; set; }
        public int? header4 { get; set; }
    }
}
