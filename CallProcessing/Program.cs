using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Net.Http;
using System.Text;
using System.Net.Http.Headers;
using System.Collections;
using Microsoft.Azure.CognitiveServices.Language.TextAnalytics;
using Microsoft.Azure.CognitiveServices.Language.TextAnalytics.Models;
using Microsoft.Rest;
using System.Threading;
using System.Data;
using CsvHelper;

namespace CallProcessing
{
    class Program
    {
        //Azure Storage account (in https://portal.azure.com, see your Storage service's Overview section)
        private const string StorageConnectionString = "<your storage connection string>";
        
        // Create the two containers in Azure Storage. 
        // Copy .mp3 files to this container
        private const string Input_container_name = "audio";
        //CSV output will be written here
        private const string Output_container_name = "output";

        // Update with your speech service region (in https://portal.azure.com, see your Speech Service's Overview section)
        private const string HostName = "<YourServiceRegion>.cris.ai";
        private const string Speech_SubscriptionKey = "<YourServiceKey>";
        private const int Port = 443;
        // recordings and locale
        private const string Locale = "en-US";

        // For usage of baseline models, no acoustic and language model needs to be specified.
        private static Guid[] modelList = new Guid[0];

        //transcription name and description
        private const string Name = "Call center sample";
        private const string Description = "Transcribing call center recordings";
        private const int TranscriptionWaitTime = 50;//seconds
        static Dictionary<string, string> transcriptionResults = new Dictionary<string, string>();

        //Update with your region for your Text Analytics subscription (in https://portal.azure.com, see your Text Service's Overview section)
        private const string TA_Entpoint = "https://<YourServiceRegion>.api.cognitive.microsoft.com";
        private const string TextAnalytics_SubscriptionKey = "<YourServiceKey>";

        static void Main(string[] args)
        {
            try
            {
                //Get list of blob SAS URIs for the audio files
                List<string> fileUriList = GetFileListFromStorage();
                Console.WriteLine("Got file URIs: " + fileUriList.Count.ToString());

                //Process the files
                TranscribeAsync(fileUriList).Wait();
                
                Console.WriteLine("Done with all transcription.");
                Console.ReadLine();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                Console.ReadLine();
            }
        }
        //Get list of mp3 files from storage
        static List<string> GetFileListFromStorage()
        {
            var listOfFileNames = new List<string>();
            // Check whether the connection string can be parsed.
            CloudStorageAccount storageAccount;
            if (CloudStorageAccount.TryParse(StorageConnectionString, out storageAccount))
            {
                //Get hold of the container
                storageAccount = CloudStorageAccount.Parse(StorageConnectionString);
                CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
                CloudBlobContainer cloudBlobContainer = blobClient.GetContainerReference(Input_container_name);

                //Loop through files
                foreach (IListBlobItem item in cloudBlobContainer.ListBlobs(null, false, BlobListingDetails.None))
                {
                    if (item.GetType() == typeof(CloudBlockBlob))
                    {
                        CloudBlockBlob blob = (CloudBlockBlob)item;
                        // Get blob SAS URI
                        //Set the expiry time and permissions for the blob.
                        //In this case, the start time is specified as a few minutes in the past, to mitigate clock skew.
                        //The shared access signature will be valid immediately.
                        SharedAccessBlobPolicy sasConstraints = new SharedAccessBlobPolicy();
                        sasConstraints.SharedAccessStartTime = DateTimeOffset.UtcNow.AddMinutes(-5);
                        sasConstraints.SharedAccessExpiryTime = DateTimeOffset.UtcNow.AddHours(24);
                        sasConstraints.SharedAccessExpiryTime = DateTimeOffset.UtcNow.AddMonths(6);
                        sasConstraints.Permissions = SharedAccessBlobPermissions.Read;

                        //Generate the shared access signature on the blob, setting the constraints directly on the signature.
                        string sasBlobToken = blob.GetSharedAccessSignature(sasConstraints);

                        //Return the URI string for the container, including the SAS token.
                        string blobSasUri = blob.Uri + sasBlobToken;
                        //Add filename to list
                        listOfFileNames.Add(blobSasUri);
                    }
                }
            }
            return listOfFileNames;
        }
        //Use speech service to convert mp3 to text
        static async Task TranscribeAsync(List<string> fileUriList)
        {
            Console.WriteLine("Starting transcriptions client...");
            //Timer
            var watch = System.Diagnostics.Stopwatch.StartNew();

            // create the client object and authenticate
            var client = BatchClient.BatchClient.CreateApiV2Client(Speech_SubscriptionKey, HostName, Port);

            // get all transcriptions for the subscription
            var transcriptions = await client.GetTranscriptionsAsync().ConfigureAwait(false);

            Console.WriteLine("Deleting all existing completed transcriptions.");
            // delete all pre-existing completed transcriptions. If transcriptions are still running or not started, they will not be deleted
            foreach (var item in transcriptions)
            {
                // delete a transcription
                await client.DeleteTranscriptionAsync(item.Id).ConfigureAwait(false);
            }

            Console.WriteLine("Creating transcriptions.");

            var createdTranscriptions = new List<Guid>();
            foreach (string fileUri in fileUriList)
            {
                //Add to transcriptions
                var transcriptionLocation = await client.PostTranscriptionAsync(Name, Description, Locale, new Uri(fileUri), modelList).ConfigureAwait(false);
                // get the transcription Id from the location URI
                createdTranscriptions.Add(new Guid(transcriptionLocation.ToString().Split('/').LastOrDefault()));
            }
            // Text Analytics client
            var credentials = new ApiKeyServiceClientCredentials(TextAnalytics_SubscriptionKey);
            var ta_client = new TextAnalyticsClient(credentials)
            {
                Endpoint = TA_Entpoint
            };         

            Console.WriteLine("Checking status.");

            // check for the status of our transcriptions every 30 sec. (can also be 1, 2, 5 min depending on usage)
            int completed = 0, running = 0, notStarted = 0;
            while (completed < fileUriList.Count)
            {
                // <batchstatus>
                // get all transcriptions for the user
                transcriptions = await client.GetTranscriptionsAsync().ConfigureAwait(false);

                completed = 0; running = 0; notStarted = 0;
                // for each transcription in the list we check the status
                foreach (var transcription in transcriptions)
                {
                    switch (transcription.Status)
                    {
                        case "Failed":
                            Console.WriteLine("Failed");
                            break;
                        case "Succeeded":
                            // we check to see if it was one of the transcriptions we created from this client.
                            if (!createdTranscriptions.Contains(transcription.Id))
                            {
                                // not created form here, continue
                                continue;
                            }
                            completed++;

                            // if the transcription was successfull, check the results
                            if (transcription.Status == "Succeeded")
                            {
                                //Output
                                List<Sentence> outSentenceList = new List<Sentence>();

                                List<string> json = new List<string>();
                                //Process 1 or more channels
                                for(int i = 0; i < transcription.ResultsUrls.Count; i++)
                                {
                                    string channelName = "channel_" + i.ToString();
                                    string result = ProcessSpeechOuput(transcription, channelName);
                                    json.Add(result);
                                }
                                //Merge the channels' output
                                List<SegmentResults> conversation = ParseAndMergeJson(json);
                                Console.WriteLine($"Sorting {conversation.Count()} lines");
                                conversation.Sort();

                                int sentenceCnt_agent = 1;
                                int sentenceCnt_cust = 1;
                                //start
                                StringBuilder sb = new StringBuilder();
                                String csvHeader = "id,caller,time,text,sentiment_score,sentiment,keyphrases";
                                sb.AppendLine(csvHeader);

                                foreach (var c in conversation)
                                {
                                    var time = TimeSpan.FromMilliseconds(c.Offset / 10000); //From time offset in 100-nanosecond units
                                    string newLine = string.Empty;
                                    string sentiment = string.Empty;
                                    string kpe = string.Empty;

                                    //Create Output Json object for PowerBI
                                    Sentence outSentence = new Sentence();
                                    Sentiment outSentiment = new Sentiment();

                                    //Check for which channel is talking (stereo file creates two channels)
                                    int id = c.id;
                                    if (id == 0)//usually the agent
                                    {
                                        outSentence = await CreateOutputCSV("agent",sentenceCnt_agent.ToString(), ta_client, c, time);
                                        sentenceCnt_agent++;
                                    }
                                    else//usually the customer
                                    {
                                        outSentence = await CreateOutputCSV("customer",sentenceCnt_cust.ToString(), ta_client, c, time);
                                        sentenceCnt_cust++;
                                    }
                                    //Add to output
                                    outSentenceList.Add(outSentence);
                                }
                                Console.WriteLine($"Success, {conversation.Count()} lines processed.");
                                //Set filename
                                string fName = transcription.RecordingsUrl.LocalPath.Split('/').LastOrDefault().Split('.').First();
                                //Save as CSV file
                                await SaveCSVtoStorageAsync(fName, outSentenceList);

                                //Stop the timer
                                watch.Stop();
                                var elapsedMs = watch.ElapsedMilliseconds;
                                Console.WriteLine(value: "Elapsed time: " + elapsedMs.ToString());
                            }
                            break;

                        case "Running":
                            running++;
                            break;

                        case "NotStarted":
                            notStarted++;
                            break;
                    }
                }
                // </batchstatus>

                Console.WriteLine(string.Format("Transcriptions status: {0} completed, {1} running, {2} not started yet", completed, running, notStarted));
                await Task.Delay(TimeSpan.FromSeconds(TranscriptionWaitTime)).ConfigureAwait(false);
            }
        }
        //Call sentiment and key phrase APIs and process results
        private static async Task<Sentence> CreateOutputCSV(string caller,string sentenceId, TextAnalyticsClient ta_client, SegmentResults c, TimeSpan time)
        {
            string script;
            Sentence outSentence = new Sentence();
            Sentiment outSentiment = new Sentiment();

            outSentence.sentence = sentenceId;
            outSentence.speaker = caller;
            outSentence.time = time.ToString();
            script = c.NBest[0].Display;
            outSentence.text = script;

            //Sentiment
            double? sentimentScore = await SentimentAnalysisAsync(ta_client, script);
            outSentiment.sentimentscore = sentimentScore.ToString();
            if (sentimentScore > 0.6)
                outSentiment.sentimenttext = "positive";
            else if (sentimentScore < 0.4)
                outSentiment.sentimenttext = "negative";
            else
                outSentiment.sentimenttext = "neutral";
            outSentence.sentiment = outSentiment;

            //KPE
            List<string> kpResult = await KeyPhraseAsync(ta_client, script);

            StringBuilder kpeSb = new StringBuilder();
            foreach (string s in kpResult)
            {
                kpeSb.Append(s + ",");
            }
            string kpeSbString = kpeSb.ToString().TrimEnd(',');
            outSentence.keyphrases = kpeSbString;

            return outSentence;
        }
        //Write the transcription to file
        private static string ProcessSpeechOuput(BatchClient.Transcription transcription, string channel)
        {
            var resultsUri = transcription.ResultsUrls[channel];

            //Get the input file name to use it for naming the output file later
            string inputFileName = transcription.RecordingsUrl.LocalPath.Split('/').LastOrDefault().Split('.')[0];
            inputFileName += "_" + channel;//add the channel to the name

            WebClient webClient = new WebClient();
            var tempFileName = Path.GetTempFileName();
            webClient.DownloadFile(resultsUri, tempFileName);
            Console.WriteLine("Downloaded " + tempFileName);
            var results = File.ReadAllText(tempFileName);
            return results;
        }
        //Merges 2 jsons, marking speakers and sorting by timestamp
        public static List<SegmentResults> ParseAndMergeJson(List<string> json)
        {
            //Find out if split channel transcription will come as AudioFileResults[i] members
            List<SegmentResults> conversation = new List<SegmentResults>();

            int i = 0;
            foreach (string j in json)
            {
                Transcript transcript = JsonConvert.DeserializeObject<Transcript>(j);
                List<SegmentResults> channel = (from s in transcript.AudioFileResults[0].SegmentResults select s).ToList();
                foreach (SegmentResults c in channel)
                {
                    c.id = i; //Set channel identifier Speaker0/Speaker1
                    conversation.Add(c);
                }
                i++;
            }
            return conversation;
        }
        //Get sentiment
        public static async Task<double?> SentimentAnalysisAsync(TextAnalyticsClient client,string input)
        {

            //The documents to be analyzed. Add the language of the document. The ID can be any value.
            var sentimentResults = await client.SentimentAsync(
                false,
                new MultiLanguageBatchInput(
                    new List<MultiLanguageInput>
                    {
                        new MultiLanguageInput("en", "1", input)
                    }));

            //Return the results
            return sentimentResults.Documents[0].Score;
        }
        //Call key phrase service
        public static async Task<List<string>> KeyPhraseAsync(TextAnalyticsClient client, string input)
        {
            //The documents to be analyzed. Add the language of the document. The ID can be any value.
            var kpResults = await client.KeyPhrasesAsync(
                false,
                new MultiLanguageBatchInput(
                    new List<MultiLanguageInput>
                    {
                        new MultiLanguageInput("en", "1", input)
                    }));

            //Return the results
            List<string> kpList = new List<string>();
            // Printing keyphrases
            foreach (var document in kpResults.Documents)
            {
                foreach (string keyphrase in document.KeyPhrases)
                {
                    kpList.Add(keyphrase);
                }
            }
            return kpList;
        }
        //Write csv file to blob storage       
        static async Task SaveCSVtoStorageAsync(string fileName, List<Sentence> sentenceOutput)
        {
            // Check whether the connection string can be parsed.
            if (CloudStorageAccount.TryParse(StorageConnectionString, out CloudStorageAccount storageAccount))
            {
                //Get hold of the container
                storageAccount = CloudStorageAccount.Parse(StorageConnectionString);
                CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
                // If the connection string is valid, proceed with operations against Blob storage here.
                CloudBlobContainer cloudBlobContainer = blobClient.GetContainerReference(Output_container_name);

                // Create a file in your local MyDocuments folder to upload to a blob.
                string localPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                //Create output file name
                string localFileName = fileName + "_output" + ".csv";
                Console.WriteLine("Outputfile: " + localFileName);
                string sourceFile = Path.Combine(localPath, localFileName);

                TextWriter textWriter = File.CreateText(sourceFile);

                using (var csvWriter = new CsvWriter(textWriter))
                {
                    csvWriter.WriteRecords(sentenceOutput);
                }

                Console.WriteLine("Temp file = {0}", sourceFile);
                Console.WriteLine("Uploading to Blob storage as blob '{0}'", localFileName);

                // Get a reference to the blob address, then upload the file to the blob.
                // Use the value of localFileName for the blob name.
                CloudBlockBlob cloudBlockBlob = cloudBlobContainer.GetBlockBlobReference(localFileName);
                await cloudBlockBlob.UploadFromFileAsync(sourceFile);
                Console.WriteLine("Uploaded " + fileName + " to storage.");
            }
            else
            {
                // Otherwise, let the user know that they need to define the environment variable.
                Console.WriteLine("A connection string has not been defined in the system environment variables.");
            }
        }
    }
    /// <summary>
    /// Allows authentication to the API using a basic apiKey mechanism
    /// </summary>
    class ApiKeyServiceClientCredentials : ServiceClientCredentials
    {
        private readonly string subscriptionKey;

        /// <summary>
        /// Creates a new instance of the ApiKeyServiceClientCredentails class
        /// </summary>
        /// <param name="subscriptionKey">The subscription key to authenticate and authorize as</param>
        public ApiKeyServiceClientCredentials(string subscriptionKey)
        {
            this.subscriptionKey = subscriptionKey;
        }

        /// <summary>
        /// Add the Basic Authentication Header to each outgoing request
        /// </summary>
        /// <param name="request">The outgoing request</param>
        /// <param name="cancellationToken">A token to cancel the operation</param>
        public override Task ProcessHttpRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request == null)
                throw new ArgumentNullException("request");

            request.Headers.Add("Ocp-Apim-Subscription-Key", this.subscriptionKey);
            return base.ProcessHttpRequestAsync(request, cancellationToken);
        }
    }
    //Transcript Class definition
    public class Transcript
    {
        public List<AudioFileResults> AudioFileResults { get; set; }
    }
    public class AudioFileResults
    {
        public string AudioFileName { get; set; }
        public List<SegmentResults> SegmentResults { get; set; }
    }
    public class SegmentResults : IComparable<SegmentResults>
    {
        public int id { get; set; }
        public string RecognitionStatus { get; set; }
        public long Offset { get; set; }
        public long Duration { get; set; }
        public List<NBest> NBest { get; set; }
        public int CompareTo(SegmentResults compareSegmentResults)
        {
            if (compareSegmentResults == null)
                return 1;
            else
                return Offset.CompareTo(compareSegmentResults.Offset);
        }
    }
    public class NBest
    {
        public float Confidence { get; set; }
        public string Lexical { get; set; }
        public string ITN { get; set; }
        public string MaskedITN { get; set; }
        public string Display { get; set; }
    }
    //Key phrase extranction
    public class TAInputDocument
    {
        public string language { get; set; }
        public string id { get; set; }
        public string text { get; set; }
    }
    public class TADocumentsRoot
    {
        public List<TAInputDocument> documents { get; set; }
    }
    //TA Key phrase output
    public class KPEDocument
    {
        public string id { get; set; }
        public List<string> keyPhrases { get; set; }
    }
    public class KPERootObject
    {
        public List<KPEDocument> documents { get; set; }
        public List<object> errors { get; set; }
    }
    //JSON classes 
    public class Sentiment
    {
        public string sentimenttext { get; set; }
        public string sentimentscore { get; set; }
    }
    public class Sentence
    {
        public string sentence { get; set; }
        public string speaker { get; set; }
        public string text { get; set; }
        public string time { get; set; }
        public Sentiment sentiment { get; set; }
        public string keyphrases { get; set; }
    }
}
