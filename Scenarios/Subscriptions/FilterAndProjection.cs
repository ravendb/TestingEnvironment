using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Raven.Client.Documents.BulkInsert;
using Raven.Client.Documents.Subscriptions;
using TestingEnvironment.Client;

namespace Subscriptions
{
    class FilterAndProjection : BaseTest
    {
        private static int[] _shipper = new int[10];
        private static Guid[] _productsGuid = new Guid[16];
        private static Guid[] _shipperGuid = new Guid[10];
        private LinkedList<Task> _tasks = new LinkedList<Task>();
        private static Guid GenralGuid = Guid.NewGuid();

        public FilterAndProjection(string orchestratorUrl, string testName) : base(orchestratorUrl, testName, "Efrat")
        {
        }

        public override void RunActualTest()
        {
            using (DocumentStore.Initialize())
            {
                ReportInfo("Inserting products docs");
                InsertProducts();

                ReportInfo("Inserting shippers docs");
                InsertShippers();

                ReportInfo("Bulk insert users docs");
                var bulkInsertUsersTask = Task.Run(() => BulkInsertUsersDocuments());
                _tasks.AddLast(bulkInsertUsersTask);

                try
                {
                    var usersSubscription = new SubscriptionCreationOptions<User2>
                    {
                        Name = $"UsersSubscription.{GenralGuid}",
                        Filter = x => (x.Age % 2) == 0
                    };

                    var orderSubscription = new SubscriptionCreationOptions<User2>
                    {
                        Name = $"CreateOrderSubscription.{GenralGuid}",
                        Filter = x => (x.Products != null),
                        Projection = x => new
                        {
                            ProductsNames = x.Products
                        }
                    };

                    ReportInfo("Create subscriptions : UsersSubscription");
                    var createUsersSubscription = DocumentStore.Subscriptions.Create(usersSubscription);

                    ReportInfo("Create subscriptions : CreateOrderSubscription");
                    var createOrderSubscription = DocumentStore.Subscriptions.Create(orderSubscription);

                    var usersSubscriptionWorkerOptions = new SubscriptionWorkerOptions(createUsersSubscription)
                    {
                        TimeToWaitBeforeConnectionRetry = TimeSpan.FromSeconds(5),
                        MaxDocsPerBatch = 20,
                        CloseWhenNoDocsLeft = false
                    };

                    var orderSubscriptionWorkerOptions = new SubscriptionWorkerOptions(createOrderSubscription)
                    {
                        TimeToWaitBeforeConnectionRetry = TimeSpan.FromSeconds(5),
                        MaxDocsPerBatch = 3,
                        CloseWhenNoDocsLeft = false
                    };

                    ReportInfo("UsersSubscription: get subscriptions worker");
                    var usersSubscriptionWorker = DocumentStore.Subscriptions.GetSubscriptionWorker<dynamic>(usersSubscriptionWorkerOptions);

                    ReportInfo("CreateOrderSubscription: get subscriptions worker");
                    var orderSubscriptionWorker = DocumentStore.Subscriptions.GetSubscriptionWorker<dynamic>(orderSubscriptionWorkerOptions);

                    ReportInfo("Start inserting products to users");
                    var usersSubscriptionRun = Task.Run(() => InsertProductsToUsers(usersSubscriptionWorker));
                    _tasks.AddLast(usersSubscriptionRun);

                    ReportInfo("Start creating orders");
                    var orderSubscriptionRun = Task.Run(() => CreateOrderDoc(orderSubscriptionWorker));
                    _tasks.AddLast(orderSubscriptionRun);

                    var i = 0;
                   
                    foreach (var shipper in _shipperGuid)
                    {
                        var shipperSubscription = new SubscriptionCreationOptions<Order>
                        {
                            Name = $"shipperSubscription-{i}.{GenralGuid}",
                            Filter = x => (x.ShipVia == $"shipper.{GenralGuid}-{shipper}"),
                        };

                        ReportInfo($"Create subscriptions : shipperSubscription-{i}.{GenralGuid}");
                        var createShipperSubscription = DocumentStore.Subscriptions.Create(shipperSubscription);

                        var shipperSubscriptionWorkerOptions = new SubscriptionWorkerOptions(createShipperSubscription)
                        {
                            TimeToWaitBeforeConnectionRetry = TimeSpan.FromSeconds(5),
                            MaxDocsPerBatch = 6
                        };

                        ReportInfo($"shipperSubscription-{i}.{GenralGuid}: get subscriptions worker");
                        var shipperSubscriptionWorker = DocumentStore.Subscriptions.GetSubscriptionWorker<Order>(shipperSubscriptionWorkerOptions);

                        var i1 = i;
                        ReportInfo($"Start counting orders for shipper {i}. Id: {_shipperGuid[i]}");
                        var getShipperTask = Task.Run(() => GetShipper(shipperSubscriptionWorker, i1));
                        _tasks.AddLast(getShipperTask);
                        i += 1;
                    }

                }
                catch (Exception e)
                {
                   ReportFailure("Error:", e); 
                }

                while (_tasks.All(x => (x.IsCompleted || x.IsCanceled)) == false)
                {
                    
                }
                ReportSuccess("Done");
            }
        }

        private void InsertProducts()
        {
            Guid guid;
            using (var session = DocumentStore.OpenSession())
            {
                for (int i = 0; i < 16; i++)
                {
                    guid = Guid.NewGuid();
                    session.Store(new Product
                    {
                        Name = $"prod-{i}",
                        PricePerUnit = i * 5,
                        QuantityPerUnit = i * 3
                    }, $"products.{GenralGuid}/{guid}");
                    _productsGuid[i] = guid;
                    session.SaveChanges();
                }
            }
            ReportInfo($"Finish inserting products docs");
        }

        private void InsertShippers()
        {
            Guid guid;
            using (var session = DocumentStore.OpenSession())
            {
                for (int i = 0; i < 10; i++)
                {
                    guid = Guid.NewGuid();
                    session.Store(new Shipper
                    {
                        Name = $"shipper.{GenralGuid}-{i}"
                    }, $"shipper.{GenralGuid}/{guid}");
                    _shipperGuid[i] = guid;
                    session.SaveChanges();
                }
                ReportInfo($"Finish inserting shippers docs");
            }
        }

        private void BulkInsertUsersDocuments()
        {
            Guid guid;
            using (BulkInsertOperation bulkInsert = DocumentStore.BulkInsert())
            {
                for (int i = 1; i <= 5000; i++)
                {
                    guid = Guid.NewGuid();
                    bulkInsert.Store(new User2
                    {
                        Id = $"user2.{GenralGuid}/{guid}",
                        FirstName = $"firstName{i}",
                        LastName = $"lastName{i}",
                        Age = i
                    }, $"user2.{GenralGuid}/{guid}");
                }
                ReportInfo($"Finish inserting users docs");
            }
        }

        private void InsertProductsToUsers(SubscriptionWorker<dynamic> subscription)
        {
            var rand = new Random();
            var ct = new CancellationTokenSource(new TimeSpan(0, 30, 0));
            int min = 0;
            subscription.Run(batch =>
            {
                foreach (var doc in batch.Items)
                {
                    if (doc.Id.StartsWith($"user2.{GenralGuid}") == false)
                        continue;
                    if (min >= doc.Result.Age)
                        continue;
                    min = doc.Result.Age;

                    var randNumber1 = rand.Next(1, 6);
                    for (var i = 0; i < randNumber1; i++)
                    {
                        var randNumber2 = rand.Next(0, 3);
                        var randNumber3 = rand.Next(0, 5);
                        if (doc.Result.Products == null)
                            doc.Result.Products = new LinkedList<string>();
                        doc.Result.Products.AddFirst($"products.{GenralGuid}/{_productsGuid[randNumber2 * randNumber3]}");
                    }
                    using (var session = DocumentStore.OpenSession())
                    {
                        session.Store(doc.Result);
                        session.SaveChanges();
                    }
                    if (doc.Result.Age == 5000)
                        ct.Cancel();

                }
            }, ct.Token);
        }

        private void CreateOrderDoc(SubscriptionWorker<dynamic> subscription)
        {
            var rand = new Random();
            var ct = new CancellationTokenSource(new TimeSpan(0, 60, 0));
            subscription.Run(batch =>
            {
                foreach (var doc in batch.Items)
                {
                    /*if (doc.Id.StartsWith($"user2.{GenralGuid}") == false)
                    {
                        ReportInfo($"DocID: {doc.Id}");
                        continue;
                    }*/
                        
                    using (var session = DocumentStore.OpenSession())
                    {
                        var shipper = rand.Next(0, 10);
                        var list = new LinkedList<string>();
                        var x = (doc.Result.ProductsNames as JArray).GetEnumerator();

                        while (x.MoveNext())
                        {
                            list.AddFirst(x.Current.ToString());
                        }
                        
                        var user = new Order
                        {
                            ShipVia = $"shipper.{GenralGuid}-{_shipperGuid[shipper]}",
                            ShipTo = doc.Id,
                            ProductsNames = list
                        };
                        session.Store(user,$"order.{GenralGuid}/");
                        _shipper[shipper] += 1;
                        session.SaveChanges();
                    }
                }
            }, ct.Token);
        }

        private static int sum = 0;
        private void GetShipper(SubscriptionWorker<Order> subscription, int i)
        {
            var count = 0;
            var ct = new CancellationTokenSource(new TimeSpan(0, 60, 0));

            subscription.Run(batch =>
            {
                foreach (var doc in batch.Items)
                {
                    if (doc.Id.StartsWith($"order.{GenralGuid}") == false)
                        continue;
                    count += 1;
                    sum += 1;
                }
                
            }, ct.Token);
            while (sum < 2500)
            {
                Console.WriteLine(sum);

            }

            ReportInfo("Done collecting info");
            if (_shipper[i] == count)
                ReportSuccess($"shipper {i} - success");
        }

        internal class User2
        {
            public string Id { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }
            public int Age { get; set; }
            public LinkedList<string> Products { get; set; }
        }

        internal class Product
        {
            public string Name { get; set; }
            public float PricePerUnit { get; set; }
            public float QuantityPerUnit { get; set; }
        }

        internal class Order
        {
            public string ShipTo { get; set; }
            public LinkedList<string> ProductsNames { get; set; }
            public string ShipVia { get; set; }
        }

        internal class Shipper
        {
            public string Name { get; set; }
        }
    }
}
