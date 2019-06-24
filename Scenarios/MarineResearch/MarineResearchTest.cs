using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using Raven.Client.Documents.Commands;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Operations.Indexes;
using TestingEnvironment.Client;
using TestingEnvironment.Common;

namespace MarineResearch
{
    public class MarineResearchTest : BaseTest
    {
        private readonly Random _mRandom = new Random();

        public MarineResearchTest(string orchestratorUrl, string testName, int round) : base(orchestratorUrl, testName, "Haludi", round)
        {
        }

        public override void RunActualTest()
        {
            using (DocumentStore.Initialize())
            {
                RunActualTestAsync().GetAwaiter().GetResult();
            }
        }

        internal async Task RunActualTestAsync()
        {
            var guid = Regex.Replace(Guid.NewGuid().ToString(), "-", "");

            var collection = $"C{guid}";
            var outputCollection = $"DailyReport{guid}";
            var indexName = $"Index{guid}";

            await CreateIndex(indexName, collection, outputCollection).ConfigureAwait(false);

            var expected = await MeasurementUploading(collection).ConfigureAwait(false);

            var csvStream = await ExportCsvDailyReport(outputCollection).ConfigureAwait(false);
            var _ = ToList(csvStream); // actual

            var groupBy = GroupByTime(expected);

            var result = CheckResult(groupBy, expected);
            if (result.Any())
                ReportFailure("Results were _not_ received as expected", null, result);

            ReportSuccess("Results were received as expected");
        }

        private Dictionary<string, string> CheckResult(IEnumerable<Measurement> actual, IEnumerable<Measurement> expected)
        {
            var actualArray = actual.ToArray();
            var expectedArray = expected.ToArray();
            var result = new Dictionary<string, string>();

            var result1 = GetUnacceptedResultsIndexes(actualArray, expectedArray);
            if (string.IsNullOrEmpty(result1))
                result.Add("Unaccepted Results Indexes", result1);

            var result2 = GetUnexpectedResults(actualArray, expectedArray);
            if (string.IsNullOrEmpty(result2))
                result.Add("Unexpected Results", result1);

            return result;
        }

        private static string GetUnexpectedResults(Measurement[] actual, Measurement[] expected)
        {
            var result = "[";
            for (var i = 0; i < actual.Length; i++)
            {
                if (expected.Any(m1 => m1.Equals(actual[i])) == false)
                    result += $"{{index:{i},  measurement:{actual[i]}}},";
            }
            result += ']';

            return result == "[]" ? null : result;
        }

        private static string GetUnacceptedResultsIndexes(Measurement[] actual, Measurement[] expected)
        {
            var result = "";

            for (var i = 0; i < expected.Length; i++)
            {
                if (actual.Any(m1 => m1.Equals(expected[i])) == false)
                    result += $"{i},";
            }

            return result;
        }

        private static Measurement[] GroupByTime(IEnumerable<Measurement> expected)
        {
            Measurement GroupFunc(DateTime key, IEnumerable<Measurement> measurements)
            {
                var enumerable = measurements as Measurement[] ?? measurements.ToArray();
                return new Measurement(
                    key,
                    enumerable.Average(m => m.Temperature),
                    enumerable.Average(m => m.Salinity)
                );
            }

            return expected.GroupBy(m => m.Time, m => m, GroupFunc).ToArray();
        }

        private static List<Measurement> ToList(Stream csvStream)
        {
            TextReader tr = new StreamReader(csvStream);
            var csv = new CsvReader(tr);
            csv.Read();
            var list = new List<Measurement>();
            while (csv.Read())
            {
                csv.TryGetField(1, out DateTime date);
                csv.TryGetField(2, out double temperature);
                csv.TryGetField(3, out double salinity);
                list.Add(new Measurement(date, temperature, salinity));
            }

            return list;
        }

        private async Task<Stream> ExportCsvDailyReport(string outputCollection)
        {
            var query = $@" from {outputCollection} as c
                            select 
                            {{
                                Day: c.Day,
                                Temperature: c.Temperature,
                                Salinity: c.Salinity,
                            }}";

            HttpClient client;
            if (DocumentStore.Certificate != null)
            {
                var clientHandler = new HttpClientHandler();
                clientHandler.ClientCertificates.Add(DocumentStore.Certificate);
                clientHandler.ClientCertificateOptions = ClientCertificateOption.Manual;
                client = new HttpClient(clientHandler);
            }
            else
            {
                client = new HttpClient();
            }

            var url =
                $"{DocumentStore.Urls[0]}/databases/{DocumentStore.Database}/streams/queries?format=csv&query={Uri.EscapeDataString(query)}";
            Stream stream = null;
            async Task Action() => stream = await client.GetStreamAsync(url).ConfigureAwait(false);
            await Retry(5, Action, "export daily result to csv").ConfigureAwait(false);
            return stream;
        }

        private async Task Retry(int nTimes, Func<Task> action, string description)
        {
            var count = 1;
            while (true)
            {
                try
                {
                    ReportInfo($"Try to {description} ({count} of {nTimes})");

                    await action().ConfigureAwait(false);

                    ReportInfo($"Succeed to {description}");
                    break;
                }
                catch (Exception e)
                {
                    var message = $"Fail to {description} ({count} of {nTimes})";

                    if (count >= nTimes)
                    {
                        ReportFailure(message, e);
                        throw;
                    }

                    ReportFailure(message, e, new Dictionary<string, string>() { ["Warning"] = "Failed but retrying..." });
                    count++;
                }
            }
        }

        private async Task CreateIndex(string indexName, string collection, string outputCollection)
        {
            async Task Action()
            {
                await DocumentStore.Maintenance.SendAsync(new PutIndexesOperation(new IndexDefinition
                {
                    Name = indexName,
                    Maps =
                    {
                        $@"from measurement in docs.{collection} select new {{ 
                                Day = measurement.Time, 
                                Temperature = measurement.Temperature, 
                                Salinity = measurement.Salinity
                            }}"
                    },
                    Reduce = @"from result in results group result by result.Day into g select new { 
                                Day = g.Key, 
                                Temperature = g.Average(x => x.Temperature), 
                                Salinity = g.Average(x => x.Salinity)
                            }",
                    OutputReduceToCollection = outputCollection
                })).ConfigureAwait(false);
            }

            await Retry(5, Action, "add index").ConfigureAwait(false);
        }

        private async Task<List<Measurement>> MeasurementUploading(string collection)
        {
            var dateTime = new DateTime(2019, 1, 1);
            var list = new List<Measurement>();
            for (var i = 0; i < 32; i++)
            {
                var requestExecutor = DocumentStore.GetRequestExecutor();
                using (var session = DocumentStore.OpenSession())
                {
                    using (var memoryStream = new MemoryStream())
                    using (var writer = new StreamWriter(memoryStream))
                    {
                        dateTime = dateTime.AddDays(1);
                        var dailyMeasurements = WriteFindingsToCsvStream(writer, dateTime);
                        list.AddRange(dailyMeasurements);

                        var getOperationIdCommand = new GetNextOperationIdCommand();
                        await requestExecutor.ExecuteAsync(getOperationIdCommand, session.Advanced.Context).ConfigureAwait(false);
                        var operationId = getOperationIdCommand.Result;

                        memoryStream.Seek(0, SeekOrigin.Begin);
                        var csvImportCommand = new CsvImportCommand(memoryStream, collection, operationId);

                        async Task Action()
                        {
                            await requestExecutor.ExecuteAsync(csvImportCommand, session.Advanced.Context).ConfigureAwait(false);
                            var operation = new Operation(requestExecutor, () => DocumentStore.Changes(), DocumentStore.Conventions, operationId);
                            await operation.WaitForCompletionAsync().ConfigureAwait(false);
                        }
                        await Retry(5, Action, "import csv").ConfigureAwait(false);

                        memoryStream.Seek(0, SeekOrigin.Begin);
                    }
                }

                Thread.Sleep(2000);
            }

            return list;
        }

        private Measurement[] WriteFindingsToCsvStream(TextWriter writer, DateTime dateTime)
        {
            const int nMeasurement = 4;

            var measurements = new Measurement[nMeasurement];
            writer.WriteLine($"{nameof(Measurement.Time)},{nameof(Measurement.Temperature)},{nameof(Measurement.Salinity)}");
            for (var i = 0; i < nMeasurement; i++)
            {
                measurements[i] = new Measurement(
                    dateTime,
                    _mRandom.Next(100, 300) / 10.0,
                    _mRandom.Next(35, 39) / 10.0
                );
                writer.WriteLine($"{dateTime},{measurements[i].Temperature},{measurements[i].Salinity}");
            }
            writer.Flush();

            return measurements;
        }
    }
}


