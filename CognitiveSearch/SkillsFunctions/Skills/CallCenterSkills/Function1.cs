using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.CognitiveSearch.WebApiSkills;
using Microsoft.Extensions.Logging;

namespace CallCenterSkills
{
    public static class Function1
    {
        static Regex csvSplit = new Regex("(?:^|,)(\"(?:[^\"])*\"|[^,]*)", RegexOptions.Compiled);

        public static string[] SplitCSV(string input)
        {

            List<string> list = new List<string>();
            string curr = null;
            foreach (Match match in csvSplit.Matches(input))
            {
                curr = match.Value;
                if (0 == curr.Length)
                {
                    list.Add("");
                }

                list.Add(curr.TrimStart(','));
            }

            return list.ToArray();
        }
        [FunctionName("GetConversation")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log, ILogger logger, ExecutionContext executionContext)
        {
            WebApiResponseRecord output = new WebApiResponseRecord();
            string skillName = executionContext.FunctionName;
            IEnumerable<WebApiRequestRecord> requestRecords = WebApiSkillHelpers.GetRequestRecords(req);
            if (requestRecords == null)
            {
                return req.CreateErrorResponse(HttpStatusCode.BadRequest, $"{skillName} - Invalid request record array.");
            }
            try
            {
                List<string> customer = new List<string>();
                logger.LogInformation("Hello World");
                Conversation conversation = new Conversation();
                string content = (string)requestRecords.First().Data["content"];
                conversation.Turns = new List<Turn>();

                string[] lines = content.Split(new char[] { '\n' });
                int i = 0;
                foreach (string line in lines)
                {
                    if (i == 0)
                    {
                        i++;
                        continue;
                    }
                    string[] regexed = SplitCSV(line);
                    //string[] cols = line.Split(new char[] { ',' });
                    if (regexed.Length < 3)
                        continue;
                    conversation.Turns.Add(new Turn(regexed[2], regexed[1], Convert.ToInt32(regexed[0])));
                    if (regexed[1] == "customer")
                        customer.Add(regexed[2]);
                    i++;


                }
                conversation.Count = conversation.Turns.Count;

                output.RecordId = requestRecords.First().RecordId;
                output.Data["conversation"] = conversation;
                output.Data["customer"] = customer;
                WebApiSkillResponse resp = new WebApiSkillResponse();
                resp.Values = new List<WebApiResponseRecord>();
                resp.Values.Add(output);
                log.Info($"Successful Run  ");
                return req.CreateResponse(HttpStatusCode.OK, resp);
            }
            catch (Exception ex)
            {
                log.Info($"Error:  {ex.Message}");
                log.Info(ex.StackTrace);
                output.RecordId = requestRecords.First().RecordId;
                output.Errors = new List<WebApiErrorWarningContract>();
                output.Errors.Add(new WebApiErrorWarningContract() { Message = ex.Message });

                WebApiSkillResponse resp = new WebApiSkillResponse();
                resp.Values = new List<WebApiResponseRecord>();
                resp.Values.Add(output);
                return req.CreateResponse(HttpStatusCode.OK, resp);
            }




        }


    }

    public class Turn
    {
        public int SeqNbr;
        public string Speaker;
        public string Utterance;

        public Turn()
        { }
        public Turn(string utterance, string speaker, int seq)
        {
            this.Speaker = speaker;
            this.Utterance = utterance;
            this.SeqNbr = seq;
        }
    }

    public class Conversation
    {
        public List<Turn> Turns;
        public int Count;

    }



}