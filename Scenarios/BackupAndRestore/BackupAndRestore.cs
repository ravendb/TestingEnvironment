using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Operations.Backups;
using Raven.Client.Documents.Operations.CompareExchange;
using Raven.Client.ServerWide.Operations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Raven.Client.Exceptions;
using TestingEnvironment.Client;

// ReSharper disable InconsistentNaming
// ReSharper disable FieldCanBeMadeReadOnly.Local

namespace BackupAndRestore
{
    public class BackupAndRestore : BaseTest
    {
        public BackupAndRestore(string orchestratorUrl, string testName, int round, string testid) : base(orchestratorUrl, testName, "Egor", round, testid)
        { }

        private const string _chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        private const string _charsOnly = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private const string _backupPath = "tempbackups";

        private const int _numberOfActors = 150_000;
        private const int _numberOfDirectors = 100_000;
        private const int _numberOfMovies = 250_000;
        private const int _numberOfDocuments = _numberOfActors + _numberOfDirectors + _numberOfMovies;

        private const int _numberOfCompareExchange = 2_500;

        private List<MyBackup> MyBackupsList;
        private List<string> MyRestoreDbsList;

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
            MyBackupsList = new List<MyBackup>();
            MyRestoreDbsList = new List<string>();


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



            // do stuff in the background
            ReportInfo("Starting background tasks");
            tasks.Add(RunTask(ModifyActors, cts));
            tasks.Add(RunTask(ModifyDirectors, cts));
            tasks.Add(RunTask(ModifyMovies, cts));
            tasks.Add(RunTask(ModifyCompareExchange, cts));
            ReportInfo("Background tasks started");
            for (int jj = 0; jj < 10; jj++)
            // while (s.Elapsed < _runTime)
            {
                //if (MyRestoreDbsList.Count == 2)
                //{
                //    ReportInfo($"Restored {MyRestoreDbsList.Count} backups, breaking while...");
                //    break;
                //}

                var rnd = new Random();
                var num = rnd.Next(0, 100);


                if (0 <= num && num < 25) // 25%
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
                else if (25 <= num && num < 50) // 25%
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
                else if (50 <= num && num < 75) // 25%
                {
                    if (MyBackupsList.Count > 0)
                    {
                        try
                        {
                            var restore = MyBackupsList.First(x => (x.OperationStatus == OperationStatus.Completed && x.RestoreResult == RestoreResult.None));
                            ReportInfo($"Started a restore {restore.BackupTaskId}");
                            await RestoreBackup(restore).ConfigureAwait(false);
                            ReportInfo($"Restored backup task: {restore.BackupTaskId} with path: {restore.BackupPath}");
                        }
                        catch
                        {
                            ReportInfo("Cannot find un restored backup.");
                        }
                    }
                }
                else if (75 <= num && num < 82) // 7%
                {
                    var waitTime = rnd.Next(50, 1000);
                    await Task.Delay(waitTime, cts.Token).ConfigureAwait(false);
                    //ReportInfo($"Waited for {waitTime} ms, Starting modify Actors");
                    await ModifyActors().ConfigureAwait(false);

                }
                else if (82 <= num && num < 89) // 7%
                {
                    var waitTime = rnd.Next(50, 1000);
                    await Task.Delay(waitTime, cts.Token).ConfigureAwait(false);
                    //ReportInfo($"Waited for {waitTime} ms, Starting modify Directors");
                    await ModifyDirectors().ConfigureAwait(false);
                }
                else if (89 <= num && num < 95) // 6%
                {
                    var waitTime = rnd.Next(50, 1000);
                    await Task.Delay(waitTime, cts.Token).ConfigureAwait(false);
                    // ReportInfo($"Waited for {waitTime} ms, Starting modify Movies");
                    await ModifyMovies().ConfigureAwait(false);
                }
                else if (95 <= num) // 5%
                {
                    var waitTime = rnd.Next(50, 1000);
                    await Task.Delay(waitTime, cts.Token).ConfigureAwait(false);
                    //  ReportInfo($"Waited for {waitTime} ms, Starting modify CompareExchange");
                    await ModifyCompareExchange().ConfigureAwait(false);
                }
            }          

            cts.Cancel();
            await Task.WhenAll(tasks).ConfigureAwait(false);

            var success = CheckBackupStatuses();
            var clearSuccess = ClearRestoredDatabases();            

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
                    ReportFailure($@"Got Failed Restore: 
                                    ID:{MyBackupsList[i].BackupTaskId}
                                    Path:{MyBackupsList[i].BackupPath}
                                    Status: {MyBackupsList[i].OperationStatus}", null);
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

        private Task RunTask(Func<Task> task, CancellationTokenSource cts)
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

                try
                {
                    await s.SaveChangesAsync().ConfigureAwait(false);
                }
                catch (ConcurrencyException)
                {
                    // ignore, we run tasks in parallel
                }
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

                try
                {
                    await s.SaveChangesAsync().ConfigureAwait(false);
                }
                catch (ConcurrencyException)
                {
                    // ignore, we run tasks in parallel
                }
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

                try
                {
                    await s.SaveChangesAsync().ConfigureAwait(false);
                }
                catch (ConcurrencyException)
                {
                    // ignore, we run tasks in parallel
                }
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

            var retries = 0;
            while (backupStatus == null)
            {
                await Task.Delay(2000).ConfigureAwait(false);
                backupStatus = DocumentStore.Maintenance.Send(new GetPeriodicBackupStatusOperation(backupTaskId)).Status;
                if (++retries > 65*60/2) // 65 minutes
                {
                    ReportFailure($"RunBackupTask: Failed to get backup {backupTaskId} status for more then an hour (retries={retries})", null);
                    return;
                }
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
                    MyBackupsList[i].BackupPath = backupStatus.LocalBackup.BackupDirectory;
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
                var reasonableTimeForBackup = Stopwatch.StartNew();
                var succeeded = true;
                try
                {
                    ReportInfo($"Starting to restore DB: {restoreDbName}");
                    var re = DocumentStore.GetRequestExecutor(DocumentStore.Database);
                    var restoreBackupTaskCommand =
                        restoreBackupTask.GetCommand(DocumentStore.Conventions, session.Advanced.Context);
                    await re.ExecuteAsync(re.TopologyNodes.First(q => q.ClusterTag.Equals(backup.BackupStatus.NodeTag)),
                            null, session.Advanced.Context, restoreBackupTaskCommand, shouldRetry: false)
                        .ConfigureAwait(false);

                    var getOperationStateTask =
                        new GetOperationStateOperation(restoreBackupTaskCommand.Result.OperationId);
                    var getOperationStateTaskCommand =
                        getOperationStateTask.GetCommand(DocumentStore.Conventions, session.Advanced.Context);

                    await re.ExecuteAsync(re.TopologyNodes.First(q => q.ClusterTag.Equals(backup.BackupStatus.NodeTag)),
                            null, session.Advanced.Context, getOperationStateTaskCommand, shouldRetry: false)
                        .ConfigureAwait(false);

                    while (getOperationStateTaskCommand.Result == null || getOperationStateTaskCommand.Result.Status == OperationStatus.InProgress)
                    {
                        await Task.Delay(2000).ConfigureAwait(false);
                        await re.ExecuteAsync(re.TopologyNodes.First(q => q.ClusterTag.Equals(backup.BackupStatus.NodeTag)),
                                null, session.Advanced.Context, getOperationStateTaskCommand, shouldRetry: false)
                            .ConfigureAwait(false);
                        if (reasonableTimeForBackup.Elapsed > TimeSpan.FromMinutes(5))
                        {
                            ReportFailure($"Restoring DB: {restoreDbName} Failed, taskID {backup.BackupTaskId} - Waited {reasonableTimeForBackup.Elapsed} and task didn't finished", null);
                            succeeded = true; // marking as success in order to delete
                            // TODO : RavenDB-13684
                            break;
                        }
                    }
                }
                catch (RavenException re)
                {
                    if (re.InnerException is ArgumentException ae)
                        ReportInfo($"Probably somebody deleted the backup files, taskID {backup.BackupTaskId}, exception: {ae}");
                    else
                        ReportFailure($"Restoring DB: {restoreDbName} Failed, taskID {backup.BackupTaskId}", re);
                }
                catch (Exception e)
                {
                    ReportFailure($"Restoring DB: {restoreDbName} Failed, taskID {backup.BackupTaskId}", e);
                    succeeded = false;
                }

                if (succeeded)
                    MyRestoreDbsList.Add(restoreDbName);

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
            try
            {
                DocumentStore.Maintenance.Server.Send(new DeleteDatabasesOperation(
                    new DeleteDatabasesOperation.Parameters
                    {
                        DatabaseNames = MyRestoreDbsList.ToArray(),
                        HardDelete = true,
                        TimeToWaitForConfirmation = TimeSpan.FromSeconds(300)
                    }));
            }
            catch (Exception e)
            {
                ReportFailure($"Failed to clear the DBs!", e);
                return false;
            }
            finally
            {
                MyRestoreDbsList.Clear();
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
                while (i < _numberOfActors)
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
