using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Operations.Backups;
using Raven.Client.Documents.Operations.CompareExchange;
using Raven.Client.ServerWide.Operations;
using TestingEnvironment.Client;

// ReSharper disable InconsistentNaming
// ReSharper disable FieldCanBeMadeReadOnly.Local

namespace BackupAndRestore
{
    public class BackupAndRestore : BaseTest
    {
        public BackupAndRestore(string orchestratorUrl, string testName) : base(orchestratorUrl, testName, "Egor")
        { }

        private const string _chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        private const string _charsOnly = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private const string _backupPath = @"C:\backups";

        private const int _numberOfActors = 500_000;
        private const int _numberOfDirectors = 100_000;
        private const int _numberOfMovies = 1_500_000;
        private const int _numberOfDocuments = _numberOfActors + _numberOfDirectors + _numberOfMovies;

        private const int _numberOfCompareExchange = 10_000;

        private static readonly TimeSpan _runTime = TimeSpan.FromMinutes(123);

        private static List<MyBackup> MyBackupsList = new List<MyBackup>();
        private static int NumOfRestoredBackups = 0;

        public enum RestoreResult
        {
            None,
            Succeeded,
            Failed
        }

        public override void RunActualTest()
        {
            DoWork().Wait();
        }

        public async Task DoWork()
        {
            var tasks = new List<Task>();
            var cts = new CancellationTokenSource();
            var s = new Stopwatch();

            using (var session = DocumentStore.OpenSession())
            {
                var actorsCount = session.Query<Actor>().Count();
                var directorsCount = session.Query<Director>().Count();
                var moviesCount = session.Query<Movie>().Count();
                var docsCount = actorsCount + directorsCount + moviesCount;
                if (docsCount < _numberOfDocuments)
                {
                    ReportInfo("Creating Documents");
                    await AddDocs(actorsCount, directorsCount, moviesCount);
                }
                else
                {
                    ReportInfo("Skipping Creation of Documents");
                }
            }

            if (DocumentStore.Maintenance.Send(new GetDetailedStatisticsOperation()).CountOfCompareExchange < _numberOfCompareExchange)
            {
                ReportInfo("Adding Compare Exchange");
                await AddCompareExchange();
            }
            else
            {
                ReportInfo("Skipping Creation of Compare Exchange");
            }

            s.Start();

            // do stuff in the background
            ReportInfo("Starting background tasks");
            tasks.Add(RunTask(ModifyActors, cts));
            tasks.Add(RunTask(ModifyDirectors, cts));
            tasks.Add(RunTask(ModifyMovies, cts));
            tasks.Add(RunTask(ModifyCompareExchange, cts));
            ReportInfo("Background tasks started");
            while (s.Elapsed < _runTime)
            {
                if (NumOfRestoredBackups == 30)
                {
                    ReportInfo($"Restored {NumOfRestoredBackups} backups, breaking while...");
                    break;
                }

                var rnd = new Random();
                var num = rnd.Next(0, 100);


                if (0 <= num && num < 4) // 4%
                {
                    ReportInfo("Started to create a backup task");
                    var myGuid = Guid.NewGuid();
                    var backupPath = $@"{_backupPath}\{myGuid}";

                    var taskID = CreateBackupTask(backupPath).Result;

                    MyBackupsList.Add(new MyBackup
                    {
                        BackupTaskId = taskID,
                        RestoreResult = RestoreResult.None,
                        Guid = myGuid
                    });

                    ReportInfo($"Finished to create a backup task with id: {taskID}");
                }
                else if (4 <= num && num < 9) // 5%
                {
                    if (MyBackupsList.Count > 0)
                    {
                        try
                        {
                            var backup = MyBackupsList.First(x => x.IsBackupCompleted == false);
                            ReportInfo("Started a full Backup");
                            await RunBackupTask(backup.BackupTaskId);
                            ReportInfo($"Backed up taskID: {backup.BackupTaskId}, Backup Path: {backup.BackupPath}");
                        }
                        catch
                        {
                            ReportInfo("Cannot find uncompleted backup task.");
                        }
                    }
                }
                else if (9 <= num && num < 14) // 5%
                {
                    if (MyBackupsList.Count > 0)
                    {
                        try
                        {
                            var restore = MyBackupsList.First(x => (x.OperationStatus == OperationStatus.Completed && x.RestoreResult == RestoreResult.None));
                            ReportInfo("Started a restore");
                            await RestoreBackup(restore);
                            ReportInfo($"Restored backup task: {restore.BackupTaskId} with path: {restore.BackupPath}");
                        }
                        catch
                        {
                            ReportInfo("Cannot find un restored backup.");
                        }
                    }
                }
                else if (14 <= num && num < 29)
                {
                    var waitTime = rnd.Next(50, 1000);
                    await Task.Delay(waitTime, cts.Token);
                    //ReportInfo($"Waited for {waitTime} ms, Starting modify Actors");
                    await ModifyActors();

                }
                else if (29 <= num && num < 44)
                {
                    var waitTime = rnd.Next(50, 1000);
                    await Task.Delay(waitTime, cts.Token);
                    //ReportInfo($"Waited for {waitTime} ms, Starting modify Directors");
                    await ModifyDirectors();
                }
                else if (44 <= num && num < 59)
                {
                    var waitTime = rnd.Next(50, 1000);
                    await Task.Delay(waitTime, cts.Token);
                   // ReportInfo($"Waited for {waitTime} ms, Starting modify Movies");
                    await ModifyMovies();
                }
                else if (59 <= num && num < 74)
                {
                    var waitTime = rnd.Next(50, 1000);
                    await Task.Delay(waitTime, cts.Token);
                  //  ReportInfo($"Waited for {waitTime} ms, Starting modify CompareExchange");
                    await ModifyCompareExchange();
                }
            }

            if (s.Elapsed >= _runTime)
                ReportInfo("Elapsed time is bigger then run time, finishing...");

            s.Stop();

            cts.Cancel();
            await Task.WhenAll(tasks);
            CheckBackupStatuses();
            ReportInfo($"Ran for {_runTime.Minutes} mins");
            ReportSuccess("All our Backup and Restore tasks succeeded");
        }

        private void CheckBackupStatuses()
        {
            for (var i = 0; i < MyBackupsList.Count; i++)
            {
                if (MyBackupsList[i].RestoreResult == RestoreResult.Failed)
                    ReportFailure("Got Failed Restore", null);

                if (MyBackupsList[i].OperationStatus != OperationStatus.Completed)
                {
                    ReportFailure($@"Got unsuccessful backup: 
                                    ID:{MyBackupsList[i].BackupTaskId}
                                    Path:{MyBackupsList[i].BackupPath}
                                    Status: {MyBackupsList[i].OperationStatus}", null);
                }
            }
        }

        private static Task RunTask(Func<Task> task, CancellationTokenSource cts)
        {
            return Task.Run(async () =>
            {
                var rnd = new Random();

                while (cts.IsCancellationRequested == false)
                {
                    var num = rnd.Next(50, 3000);
                    await Task.Delay(num, cts.Token);

                    if (cts.IsCancellationRequested)
                        return;

                    await task();
                }
            });
        }

        private async Task ModifyActors()
        {
            const int docsToModify = 1024;
            var rnd = new Random();

            using (var s = DocumentStore.OpenAsyncSession())
            {
                var from = rnd.Next(0, _numberOfActors - docsToModify - 1);
                var actors = (await s.Advanced.LoadStartingWithAsync<Actor>(nameof(Actor), null, from, docsToModify)).ToList();

                foreach (var a in actors)
                {
                    var luckyNumber = rnd.Next(0, 2);

                    if (luckyNumber == 0)
                        a.Age++;
                    else if (luckyNumber == 1)
                        a.Age--;
                    else
                        a.FullName = $"{new string(Enumerable.Repeat(_charsOnly, rnd.Next(2, 6)).Select(x => x[rnd.Next(x.Length)]).ToArray())} {new string(Enumerable.Repeat(_charsOnly, rnd.Next(2, 6)).Select(x => x[rnd.Next(x.Length)]).ToArray())}";

                    await s.StoreAsync(a);
                }

                await s.SaveChangesAsync();
            }
        }

        private async Task ModifyDirectors()
        {
            const int docsToModify = 1024;
            var rnd = new Random();

            using (var s = DocumentStore.OpenAsyncSession())
            {
                var from = rnd.Next(0, _numberOfDirectors - docsToModify - 1);
                var directors = (await s.Advanced.LoadStartingWithAsync<Director>(nameof(Director), null, from, docsToModify))
                    .ToList();

                foreach (var d in directors)
                {
                    var luckyNumber = rnd.Next(0, 2);

                    if (luckyNumber == 0)
                        d.Age++;
                    else if (luckyNumber == 1)
                        d.Age--;
                    else
                        d.FullName =
                            $"{new string(Enumerable.Repeat(_charsOnly, rnd.Next(2, 6)).Select(x => x[rnd.Next(x.Length)]).ToArray())} {new string(Enumerable.Repeat(_charsOnly, rnd.Next(2, 6)).Select(x => x[rnd.Next(x.Length)]).ToArray())}";

                    await s.StoreAsync(d);
                }

                await s.SaveChangesAsync();
            }
        }

        private async Task ModifyMovies()
        {
            const int docsToModify = 1024;
            var rnd = new Random();

            using (var s = DocumentStore.OpenAsyncSession())
            {
                var from = rnd.Next(0, _numberOfMovies - docsToModify - 1);
                var movies = (await s.Advanced.LoadStartingWithAsync<Movie>(nameof(Movie), null, from, docsToModify))
                    .ToList();

                foreach (var m in movies)
                {
                    var luckyNumber = rnd.Next(0, 4);

                    if (luckyNumber == 0)
                        m.Year++;
                    else if (luckyNumber == 1)
                        m.Year--;
                    else if (luckyNumber == 2)
                        m.Description = new string(Enumerable.Repeat(_charsOnly, rnd.Next(32, 255)).Select(x => x[rnd.Next(x.Length)]).ToArray());
                    else if (luckyNumber == 3)
                        m.Title = new string(Enumerable.Repeat(_charsOnly, rnd.Next(1, 32)).Select(x => x[rnd.Next(x.Length)]).ToArray());
                    else
                    {
                        if (m.ActorsIds.Count > 1)
                            m.ActorsIds.RemoveAt(0);
                    }

                    await s.StoreAsync(m);
                }

                await s.SaveChangesAsync();
            }
        }

        private async Task ModifyCompareExchange()
        {
            var rnd = new Random();
            var from = rnd.Next(0, _numberOfCompareExchange - 256 - 1);
            var list = DocumentStore.Operations.SendAsync(new GetCompareExchangeValuesOperation<int>("", from, 256));

            foreach (var key in list.Result.Keys)
            {
                var val = list.Result[key].Value;
                val += 1;

                await DocumentStore.Operations.SendAsync(
                    new PutCompareExchangeValueOperation<int>(key, val, 0));
            }
        }

        private async Task<long> CreateBackupTask(string backupPath)
        {
            var rnd = new Random();

            var backupName = new string(Enumerable.Repeat(_chars, rnd.Next(1, 10))
                .Select(x => x[rnd.Next(x.Length)]).ToArray());
            var backupConfig = new PeriodicBackupConfiguration
            {
                LocalSettings = new LocalSettings
                {
                    FolderPath = backupPath
                },
                FullBackupFrequency = "0 */3 * * *",
                BackupType = BackupType.Backup,
                Name = backupName
            };

            var result = await DocumentStore.Maintenance.SendAsync(new UpdatePeriodicBackupOperation(backupConfig));
            return result.TaskId;
        }

        private async Task RunBackupTask(long backupTaskId)
        {
            await DocumentStore.Maintenance.SendAsync(new StartBackupOperation(true, backupTaskId));
            PeriodicBackupStatus backupStatus = DocumentStore.Maintenance.Send(new GetPeriodicBackupStatusOperation(backupTaskId)).Status;

            while (backupStatus == null)
            {
                await Task.Delay(2000);
                backupStatus = DocumentStore.Maintenance.Send(new GetPeriodicBackupStatusOperation(backupTaskId)).Status;
            }

            for (var i = 0; i < MyBackupsList.Count; i++)
            {
                if (MyBackupsList[i].BackupTaskId == backupTaskId)
                {
                    OperationStatus operationStatus;
                    if (backupStatus.Error == null)
                    {
                        operationStatus = backupStatus.LastFullBackup == null ? OperationStatus.Canceled : OperationStatus.Completed;
                    }
                    else
                        operationStatus = OperationStatus.Faulted;

                    MyBackupsList[i].OperationStatus = operationStatus;
                    MyBackupsList[i].BackupStatus = backupStatus;
                    MyBackupsList[i].BackupPath = $@"{_backupPath}\{MyBackupsList[i].Guid}\{backupStatus.FolderName}";
                    MyBackupsList[i].IsBackupCompleted = true;

                    break;
                }
            }
        }

        private async Task RestoreBackup(MyBackup backup)
        {
            var rnd = new Random();
            var restoreDbName =
                $"Restored_{new string(Enumerable.Repeat(_chars, rnd.Next(5, 10)).Select(x => x[rnd.Next(x.Length)]).ToArray())}";

            var restoreConfig = new RestoreBackupConfiguration
            {
                DatabaseName = restoreDbName,
                BackupLocation = backup.BackupPath
            };
            var restoreBackupTask = new RestoreBackupOperation(restoreConfig);

            using (var session = DocumentStore.OpenAsyncSession())
            {
                var succeeded = true;
                try
                {
                    ReportInfo($"Starting to restore DB: {restoreDbName}");
                    var re = DocumentStore.GetRequestExecutor(DocumentStore.Database);
                    var restoreBackupTaskCommand = restoreBackupTask.GetCommand(DocumentStore.Conventions, session.Advanced.Context);
                    await re.ExecuteAsync(re.TopologyNodes.First(q => q.ClusterTag == backup.BackupStatus.NodeTag),
                        null, session.Advanced.Context, restoreBackupTaskCommand, shouldRetry: false);
                    
                    var getOperationStateTask = new GetOperationStateOperation(restoreBackupTaskCommand.Result.OperationId);
                    var getOperationStateTaskCommand = getOperationStateTask.GetCommand(DocumentStore.Conventions, session.Advanced.Context);

                    await re.ExecuteAsync(re.TopologyNodes.First(q => q.ClusterTag == backup.BackupStatus.NodeTag),
                        null, session.Advanced.Context, getOperationStateTaskCommand, shouldRetry: false);

                    while (getOperationStateTaskCommand.Result.Status == OperationStatus.InProgress)
                    {
                        await Task.Delay(2000);
                        await re.ExecuteAsync(re.TopologyNodes.First(q => q.ClusterTag == backup.BackupStatus.NodeTag),
                            null, session.Advanced.Context, getOperationStateTaskCommand, shouldRetry: false);
                    }
                }
                catch (Exception e)
                {
                    ReportFailure($"Restoring DB: {restoreDbName} Failed", e);
                    succeeded = false;
                }

                for (var i = 0; i < MyBackupsList.Count; i++)
                {
                    if (MyBackupsList[i].BackupTaskId == backup.BackupTaskId)
                    {
                        MyBackupsList[i].RestoreResult = succeeded ? RestoreResult.Succeeded : RestoreResult.Failed;
                        break;
                    }
                }
            }
        }

        private async Task AddDocs(int actorsCount, int directorsCount, int moviesCount)
        {
            using (var bulkInsert = DocumentStore.BulkInsert())
            {
                var genresList = new List<string> { "Biography", "Comedy", "Drama", "Music", "Sport", "Adventure", "Family", "Fantasy", "Horror" };

                var rnd = new Random();
                int i = actorsCount;
                while(i < _numberOfActors)
                {
                    await bulkInsert.StoreAsync(new Actor
                    {
                        Id = $"Actor/{i}",
                        FullName = new string(Enumerable.Repeat(_charsOnly, rnd.Next(2, 6)).Select(x => x[rnd.Next(x.Length)]).ToArray()),
                        Age = rnd.Next(1, 99)
                    }, $"Actor/{i}");
                    i++;
                }
                ReportInfo($"Added {i - actorsCount} Actors");
                i = directorsCount;
                while (i < _numberOfDirectors)
                {
                    await bulkInsert.StoreAsync(new Director
                    {
                        FullName = new string(Enumerable.Repeat(_charsOnly, rnd.Next(2, 6)).Select(x => x[rnd.Next(x.Length)]).ToArray()),
                        Age = rnd.Next(1, 99)
                    }, $"Director/{i}");
                    i++;
                }
                ReportInfo($"Added {i - directorsCount} Directors");
                var actorsList = new List<Actor>();
                using (var s = DocumentStore.OpenAsyncSession())
                {
                    var enumerator = await s
                        .Advanced
                        .StreamAsync<Actor>("Actor/");

                    while (await enumerator.MoveNextAsync())
                        actorsList.Add(enumerator.Current.Document);

                    i = moviesCount;
                    while (i < _numberOfMovies)
                    {
                        await bulkInsert.StoreAsync(new Movie
                        {
                            Title = new string(Enumerable.Repeat(_chars, rnd.Next(1, 12)).Select(x => x[rnd.Next(x.Length)]).ToArray()),
                            Year = rnd.Next(1980, 2080),
                            Genre = genresList[rnd.Next(genresList.Count)],
                            Rating = rnd.NextDouble() * 10,
                            ActorsIds = actorsList.GetRange(rnd.Next(0, 99_964), rnd.Next(1, 32)).ConvertAll(x => x.Id),
                            Description = new string(Enumerable.Repeat(_chars, 255).Select(x => x[rnd.Next(x.Length)]).ToArray())
                        }, $"Movie/{i}");
                        i++;
                    }
                    ReportInfo($"Added {i - moviesCount} Movies");
                }
            }
        }

        private async Task AddCompareExchange()
        {
            var rnd = new Random();

            for (var i = 0; i < _numberOfCompareExchange; i++)
            {
                await DocumentStore.Operations.SendAsync(
                    new PutCompareExchangeValueOperation<int>(new string(Enumerable.Repeat(_chars, 255).Select(x => x[rnd.Next(x.Length)]).ToArray()), rnd.Next(0, int.MaxValue), 0));
            }
        }

        public class MyBackup
        {
            public string BackupPath { get; set; }
            public long BackupTaskId { get; set; }
            public OperationStatus OperationStatus { get; set; }
            public PeriodicBackupStatus BackupStatus { get; set; }
            public RestoreResult RestoreResult { get; set; }
            public Guid Guid { get; set; }
            public bool IsBackupCompleted { get; set; }
        }

        public class Movie
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public int Year { get; set; }
            public string Genre { get; set; }
            public double Rating { get; set; }
            public List<string> ActorsIds { get; set; }
            public Director Director { get; set; }
            public string Description { get; set; }
        }

        public class Actor
        {
            public string Id { get; set; }
            public int Age { get; set; }
            public string FullName { get; set; }
        }

        public class Director
        {
            public int Age { get; set; }
            public string FullName { get; set; }
        }
    }
}
