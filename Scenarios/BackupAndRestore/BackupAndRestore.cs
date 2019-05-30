using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        public BackupAndRestore(string orchestratorUrl, string testName, int round) : base(orchestratorUrl, testName, "Egor", round)
        { }

        private const string _chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        private const string _charsOnly = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private const string _backupPath = "tempbackups";

        private const int _numberOfActors = 150_000;
        private const int _numberOfDirectors = 100_000;
        private const int _numberOfMovies = 250_000;
        private const int _numberOfDocuments = _numberOfActors + _numberOfDirectors + _numberOfMovies;

        private const int _numberOfCompareExchange = 2_500;

        private static readonly TimeSpan _runTime = TimeSpan.FromMinutes(9);

        private static readonly List<MyBackup> MyBackupsList = new List<MyBackup>();
        private static readonly List<MyRestoredDB> MyRestoreDbsList = new List<MyRestoredDB>();

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
                    await AddDocs(actorsCount, directorsCount, moviesCount).ConfigureAwait(false);
                }
                else
                {
                    ReportInfo("Skipping Creation of Documents");
                }
            }

            if (DocumentStore.Maintenance.Send(new GetDetailedStatisticsOperation()).CountOfCompareExchange < _numberOfCompareExchange)
            {
                ReportInfo("Adding Compare Exchange");
                await AddCompareExchange().ConfigureAwait(false);
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
                if (MyRestoreDbsList.Count == 30)
                {
                    ReportInfo($"Restored {MyRestoreDbsList.Count} backups, breaking while...");
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
                            await RunBackupTask(backup.BackupTaskId).ConfigureAwait(false);
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
                            await RestoreBackup(restore).ConfigureAwait(false);
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
                    await Task.Delay(waitTime, cts.Token).ConfigureAwait(false);
                    //ReportInfo($"Waited for {waitTime} ms, Starting modify Actors");
                    await ModifyActors().ConfigureAwait(false);

                }
                else if (29 <= num && num < 44)
                {
                    var waitTime = rnd.Next(50, 1000);
                    await Task.Delay(waitTime, cts.Token).ConfigureAwait(false);
                    //ReportInfo($"Waited for {waitTime} ms, Starting modify Directors");
                    await ModifyDirectors().ConfigureAwait(false);
                }
                else if (44 <= num && num < 59)
                {
                    var waitTime = rnd.Next(50, 1000);
                    await Task.Delay(waitTime, cts.Token).ConfigureAwait(false);
                   // ReportInfo($"Waited for {waitTime} ms, Starting modify Movies");
                    await ModifyMovies().ConfigureAwait(false);
                }
                else if (59 <= num && num < 74)
                {
                    var waitTime = rnd.Next(50, 1000);
                    await Task.Delay(waitTime, cts.Token).ConfigureAwait(false);
                  //  ReportInfo($"Waited for {waitTime} ms, Starting modify CompareExchange");
                    await ModifyCompareExchange().ConfigureAwait(false);
                }
            }

            if (s.Elapsed >= _runTime)
                ReportInfo("Elapsed time is bigger then run time, finishing...");

            s.Stop();

            cts.Cancel();
            await Task.WhenAll(tasks).ConfigureAwait(false);

            var success = CheckBackupStatuses();
            var clearSuccess = ClearRestoredDatabases();


            ReportInfo($"Ran for {_runTime.Minutes} mins");

            if (success && clearSuccess)
                ReportSuccess("BackupAndRestore Test Finished.");
            else
                ReportFailure("BackupAndRestore Test Finished with failures", null);
        }

        private bool CheckBackupStatuses()
        {
            var success = true;
            for (var i = 0; i < MyBackupsList.Count; i++)
            {
                if (MyBackupsList[i].RestoreResult == RestoreResult.Failed)
                {
                    success = false;
                    ReportFailure("Got Failed Restore", null);
                }

                if (MyBackupsList[i].OperationStatus == OperationStatus.Faulted)
                {
                    success = false;
                    ReportFailure($@"Got unsuccessful backup: 
                                    ID:{MyBackupsList[i].BackupTaskId}
                                    Path:{MyBackupsList[i].BackupPath}
                                    Status: {MyBackupsList[i].OperationStatus}", null);
                }
            }
            if (success)
                ReportInfo("All our Backup and Restore tasks succeeded");

            return success;
        }

        private static Task RunTask(Func<Task> task, CancellationTokenSource cts)
        {
            return Task.Run(async () =>
            {
                var rnd = new Random();

                while (cts.IsCancellationRequested == false)
                {
                    var num = rnd.Next(50, 3000);

                    try
                    {
                        await Task.Delay(num, cts.Token).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        return;
                    }

                    if (cts.IsCancellationRequested)
                        return;

                    await task().ConfigureAwait(false);
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
                var actors = (await s.Advanced.LoadStartingWithAsync<Actor>(nameof(Actor), null, from, docsToModify).ConfigureAwait(false)).ToList();

                foreach (var a in actors)
                {
                    var luckyNumber = rnd.Next(0, 2);

                    if (luckyNumber == 0)
                        a.Age++;
                    else if (luckyNumber == 1)
                        a.Age--;
                    else
                        a.FullName = $"{new string(Enumerable.Repeat(_charsOnly, rnd.Next(2, 6)).Select(x => x[rnd.Next(x.Length)]).ToArray())} {new string(Enumerable.Repeat(_charsOnly, rnd.Next(2, 6)).Select(x => x[rnd.Next(x.Length)]).ToArray())}";

                    await s.StoreAsync(a).ConfigureAwait(false);
                }

                await s.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        private async Task ModifyDirectors()
        {
            const int docsToModify = 1024;
            var rnd = new Random();

            using (var s = DocumentStore.OpenAsyncSession())
            {
                var from = rnd.Next(0, _numberOfDirectors - docsToModify - 1);
                var directors = (await s.Advanced.LoadStartingWithAsync<Director>(nameof(Director), null, from, docsToModify).ConfigureAwait(false))
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

                    await s.StoreAsync(d).ConfigureAwait(false);
                }

                await s.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        private async Task ModifyMovies()
        {
            const int docsToModify = 1024;
            var rnd = new Random();

            using (var s = DocumentStore.OpenAsyncSession())
            {
                var from = rnd.Next(0, _numberOfMovies - docsToModify - 1);
                var movies = (await s.Advanced.LoadStartingWithAsync<Movie>(nameof(Movie), null, from, docsToModify).ConfigureAwait(false))
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

                    await s.StoreAsync(m).ConfigureAwait(false);
                }

                await s.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        private async Task ModifyCompareExchange()
        {
            var rnd = new Random();
            var from = rnd.Next(0, _numberOfCompareExchange - 256 - 1);
            var list = DocumentStore.Operations.SendAsync(new GetCompareExchangeValuesOperation<string>("", from, 256));

            foreach (var key in list.Result.Keys)
            {
                if (int.TryParse(list.Result[key].Value, out var val) == false)
                    continue;
                
                val += 1;

                await DocumentStore.Operations.SendAsync(
                    new PutCompareExchangeValueOperation<int>(key, val, 0)).ConfigureAwait(false);
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

            var result = await DocumentStore.Maintenance.SendAsync(new UpdatePeriodicBackupOperation(backupConfig)).ConfigureAwait(false);
            return result.TaskId;
        }

        private async Task RunBackupTask(long backupTaskId)
        {
            await DocumentStore.Maintenance.SendAsync(new StartBackupOperation(true, backupTaskId)).ConfigureAwait(false);
            PeriodicBackupStatus backupStatus = DocumentStore.Maintenance.Send(new GetPeriodicBackupStatusOperation(backupTaskId)).Status;

            while (backupStatus == null)
            {
                await Task.Delay(2000).ConfigureAwait(false);
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
                        null, session.Advanced.Context, restoreBackupTaskCommand, shouldRetry: false).ConfigureAwait(false);

                    var getOperationStateTask = new GetOperationStateOperation(restoreBackupTaskCommand.Result.OperationId);
                    var getOperationStateTaskCommand = getOperationStateTask.GetCommand(DocumentStore.Conventions, session.Advanced.Context);

                    await re.ExecuteAsync(re.TopologyNodes.First(q => q.ClusterTag == backup.BackupStatus.NodeTag),
                        null, session.Advanced.Context, getOperationStateTaskCommand, shouldRetry: false).ConfigureAwait(false);

                    while (getOperationStateTaskCommand.Result.Status == OperationStatus.InProgress)
                    {
                        await Task.Delay(2000).ConfigureAwait(false);
                        await re.ExecuteAsync(re.TopologyNodes.First(q => q.ClusterTag == backup.BackupStatus.NodeTag),
                            null, session.Advanced.Context, getOperationStateTaskCommand, shouldRetry: false).ConfigureAwait(false);
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

        private bool ClearRestoredDatabases()
        {
            if (MyRestoreDbsList.Count == 0)
            {
                ReportInfo("No Restored Databases to clear.");
                return true;
            }

            ReportInfo("Clearing Restored Databases, Please clear the backup .ravendbdump files manually!");
            var dbNames = new string[MyRestoreDbsList.Count];

            for (var i = 0; i < MyRestoreDbsList.Count; i++)
            {
                dbNames[i] = MyRestoreDbsList[i].Name;
            }

            try
            {
                DocumentStore.Maintenance.Server.Send(new DeleteDatabasesOperation(new DeleteDatabasesOperation.Parameters
                {
                    DatabaseNames = dbNames,
                    HardDelete = true,
                    TimeToWaitForConfirmation = TimeSpan.FromSeconds(300)
                }));
            }
            catch
            {
                ReportInfo("Failed to clear the DBs!");
                return false;
            }
            return true;
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
                    }, $"Actor/{i}").ConfigureAwait(false);
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
                    }, $"Director/{i}").ConfigureAwait(false);
                    i++;
                }
                ReportInfo($"Added {i - directorsCount} Directors");
                var actorsList = new List<Actor>();
                using (var s = DocumentStore.OpenAsyncSession())
                {
                    var enumerator = await s
                        .Advanced
                        .StreamAsync<Actor>("Actor/").ConfigureAwait(false);

                    while (await enumerator.MoveNextAsync().ConfigureAwait(false))
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
                        }, $"Movie/{i}").ConfigureAwait(false);
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
                    new PutCompareExchangeValueOperation<int>(new string(Enumerable.Repeat(_chars, 255).Select(x => x[rnd.Next(x.Length)]).ToArray()), rnd.Next(0, int.MaxValue), 0)).ConfigureAwait(false);
            }
        }

        public class MyRestoredDB
        {
            public string Name { get; set; }
            public string NodeTag { get; set; }
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
