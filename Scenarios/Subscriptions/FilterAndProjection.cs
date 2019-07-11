using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Raven.Client.Documents.BulkInsert;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Operations.Revisions;
using Raven.Client.Documents.Queries;
using Raven.Client.Documents.Session;
using Raven.Client.Documents.Subscriptions;
using Raven.Client.Exceptions.Documents.Subscriptions;
using Raven.Client.Http;
using Raven.Client.Util;
using Sparrow.Json;
using TestingEnvironment.Client;

namespace Subscriptions
{

    public class GetRevisionsOperation : IMaintenanceOperation<RevisionsConfiguration>
    {
        

        public GetRevisionsOperation()
        {
            
        }

        public RavenCommand<RevisionsConfiguration> GetCommand(DocumentConventions conventions, JsonOperationContext ctx)
        {
            return new GetRevisionsCommand();
        }

        private class GetRevisionsCommand : RavenCommand<RevisionsConfiguration>
        {
            

            public GetRevisionsCommand()
            {
                
            }

            public override bool IsReadRequest => false;

            public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
            {
                url = $"{node.Url}/databases/{node.Database}/revisions/config";

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get                  
                };

                return request;
            }

            public override void SetResponse(JsonOperationContext context, BlittableJsonReaderObject response, bool fromCache)
            {
                if (response == null)
                {
                    Result = new RevisionsConfiguration();
                }
                else
                {
                    Result = (RevisionsConfiguration)EntityToBlittable.ConvertToEntity(typeof(RevisionsConfiguration), null, response, new DocumentConventions());
                }
            }
            
        }
    }

   

    public class FilterAndProjection : BaseTest
    {
        private const int ShippersCount = 2;
        private const int ProductsCount = 9;
        private const int UsersCount = 5000;
        private int[] _shipper;
        private int[] _shipperRes;
        private Guid[] _productsGuid;
        private Guid[] _shipperGuid;
        private List<SubscriptionWorker<Order>> _ordersProcessingWorkers;
        private Guid GenralGuid;       


        public FilterAndProjection(string orchestratorUrl, string testName, int round, string testid) : base(orchestratorUrl, testName, "Efrat", round, testid)
        {
        }
        public int counter = 0;
        private int startsWithSkipCount = 0;        
        public override void RunActualTest()
        {
            using (DocumentStore.Initialize())
            {
                GenralGuid = Guid.NewGuid();
                _shipper = new int[ShippersCount];
                _shipperRes = new int[ShippersCount];
                _productsGuid = new Guid[ProductsCount];
                _shipperGuid = new Guid[ShippersCount];
                _ordersProcessingWorkers = new List<SubscriptionWorker<Order>>();

                var config = DocumentStore.Maintenance.Send(new GetRevisionsOperation());
                if (config.Collections == null)
                    config.Collections = new Dictionary<string, RevisionsCollectionConfiguration>();
                if (config.Collections.TryAdd("User2s", new RevisionsCollectionConfiguration
                {
                    PurgeOnDelete = true,
                    Disabled = false
                }))
                {
                    DocumentStore.Maintenance.Send(new ConfigureRevisionsOperation(config));
                }
                ReportInfo("Inserting products docs");
                InsertProducts();

                ReportInfo("Inserting shippers docs");
                InsertShippers();

                ReportInfo("Bulk insert users docs");
                var bulkInsertUsersTask = Task.Run(() => BulkInsertUsersDocuments());

                SubscriptionWorker<dynamic> workerOfUsersSubscriptionToAppendProductsToUsers = null;
                SubscriptionWorker<dynamic> workerOfUsersSubscriptionToCreateOrder = null;
                                
                var ordersProcessingCountdown = new CountdownEvent(UsersCount/2);
                var success = true;

                try
                {
                    ReportInfo("Create subscriptions : UsersSubscription");
                    var nameOFUserSubscriptionToAppendProductsToUsers = DocumentStore.Subscriptions.Create(new SubscriptionCreationOptions<User2>
                    {
                        Name = UsersSubscriptionName,
                        Filter = x => (x.Age % 2) == 0 && x.Products==null
                    });

                    ReportInfo("Create subscriptions : CreateOrderSubscription");
                    var nameOfUsersSubscriptionToCreateOrders = DocumentStore.Subscriptions.Create(new SubscriptionCreationOptions<User2>
                    {
                        Name = OrdersSubscriptionName,
                        Filter = x => (x.Products != null),
                        Projection = x => (new
                        {
                            ProductsNames = x.Products
                        })
                    });                                      

                    ReportInfo("UsersSubscription: get subscriptions worker");
                    workerOfUsersSubscriptionToAppendProductsToUsers = DocumentStore.Subscriptions.GetSubscriptionWorker<dynamic>(new SubscriptionWorkerOptions(nameOFUserSubscriptionToAppendProductsToUsers)
                    {
                        TimeToWaitBeforeConnectionRetry = TimeSpan.FromSeconds(5),
                        MaxDocsPerBatch = 20,
                        CloseWhenNoDocsLeft = false
                    });

                    ReportInfo("CreateOrderSubscription: get subscriptions worker");
                    workerOfUsersSubscriptionToCreateOrder = DocumentStore.Subscriptions.GetSubscriptionWorker<dynamic>(new SubscriptionWorkerOptions(nameOfUsersSubscriptionToCreateOrders)
                    {
                        TimeToWaitBeforeConnectionRetry = TimeSpan.FromSeconds(5),
                        MaxDocsPerBatch = 3,
                        CloseWhenNoDocsLeft = false
                    });

                    ReportInfo("Start inserting products to users");
                    InsertProductsToUsers(workerOfUsersSubscriptionToAppendProductsToUsers);

                    ReportInfo("Start creating orders");
                    CreateOrderDocFromUserProductNames(workerOfUsersSubscriptionToCreateOrder);                                        

                    for (int i = 0; i < _shipperGuid.Length; i++)
                    {
                        Guid shipper = _shipperGuid[i];
                        var s = "shipper." + GenralGuid + "/" + shipper;
                        var ordersSubscriptionForShipper = new SubscriptionCreationOptions<Order>
                        {
                            Name = $"shipperSubscription-{i}.{GenralGuid}",
                            Filter = x => (x.ShipVia.StartsWith(s))
                        };

                        ReportInfo($"Create subscriptions : shipperSubscription-{i}.{GenralGuid}");
                        var ordersSubscriptionName = DocumentStore.Subscriptions.Create(ordersSubscriptionForShipper);

                        var ordersSubscriptionWorkerOptions = new SubscriptionWorkerOptions(ordersSubscriptionName)
                        {
                            TimeToWaitBeforeConnectionRetry = TimeSpan.FromSeconds(5),
                            MaxDocsPerBatch = 6,
                            CloseWhenNoDocsLeft = false
                        };

                        ReportInfo($"shipperSubscription-{i}.{GenralGuid}: get subscriptions worker");
                        var shipperSubscriptionWorker = DocumentStore.Subscriptions.GetSubscriptionWorker<Order>(ordersSubscriptionWorkerOptions);
                                                
                        ReportInfo($"Start counting orders for shipper {i}. Id: {_shipperGuid[i]}");
                        TrackShipperDataFromOrders(shipperSubscriptionWorker, i, ordersProcessingCountdown);
                        _ordersProcessingWorkers.Add(shipperSubscriptionWorker);                        
                    }                    

                    if (false == ordersProcessingCountdown.Wait(TimeSpan.FromMinutes(10)))
                    {
                        ReportFailure("Users processing took too long", null);
                        return;
                    }

                    for (int i = 0; i < ShippersCount; i++)
                    {
                        if (_shipper[i] != _shipperRes[i])
                        {
                            ReportInfo($"{i}: {_shipper[i]} != {_shipperRes[i]}");
                            success = false;
                            break;
                        }
                    }

                    if (success)
                        ReportSuccess("Test done");
                    else
                        ReportFailure("Test Failed", null);

                }
                catch (Exception e)
                {
                   ReportFailure("Error:", e); 
                }
                // cleanup
                finally
                {
                    ordersProcessingCountdown.Dispose();
                    workerOfUsersSubscriptionToAppendProductsToUsers.Dispose();
                    workerOfUsersSubscriptionToCreateOrder.Dispose();
                    foreach (var worker in _ordersProcessingWorkers)
                    {
                        try
                        {
                            worker.Dispose();
                        }
                        catch (Exception ex)
                        {
                            ReportFailure("Subscriptions processing aborted", null);
                        }
                    }

                    if (bulkInsertUsersTask.IsCompleted == false)
                    {
                        ReportFailure("Bulk insert was not finished when it was supposed to", null);
                        bulkInsertUsersTask.Wait();
                    }
                    var cleanupTasks = new List<Task>();

                    using (var session = DocumentStore.OpenSession())
                    {
                        cleanupTasks.Add(DocumentStore.Operations.Send(new DeleteByQueryOperation(
                            new IndexQuery()
                            {
                                Query = $"from User2s where startsWith(Id(), 'user2.{GenralGuid}/')"
                            }
                            , new QueryOperationOptions
                            {
                                StaleTimeout = TimeSpan.FromMinutes(5)
                            }
                        )).WaitForCompletionAsync(TimeSpan.FromMinutes(10)));

                        cleanupTasks.Add(DocumentStore.Operations.Send(new DeleteByQueryOperation(
                             new IndexQuery()
                             {
                                 Query = $"from shippers where startsWith(Id(), 'shipper.{GenralGuid}/')"
                             }, new QueryOperationOptions
                             {
                                 StaleTimeout = TimeSpan.FromMinutes(5)
                             }
                             )).WaitForCompletionAsync(TimeSpan.FromMinutes(10)));

                        cleanupTasks.Add(DocumentStore.Operations.Send(new DeleteByQueryOperation(
                             new IndexQuery()
                             {
                                 Query = $"from orders where startsWith(Id(), 'order.{GenralGuid}/')"
                             },
                            new QueryOperationOptions
                            {
                                StaleTimeout = TimeSpan.FromMinutes(5)
                            }
                             )).WaitForCompletionAsync(TimeSpan.FromMinutes(10)));

                        cleanupTasks.Add(DocumentStore.Operations.Send(new DeleteByQueryOperation(
                             new IndexQuery()
                             {
                                 Query = $"from Products where startsWith (Id(), 'products.{GenralGuid}/')"
                             },
                            new QueryOperationOptions
                            {
                                StaleTimeout = TimeSpan.FromMinutes(5)
                            }
                             )).WaitForCompletionAsync(TimeSpan.FromMinutes(10)));
                    }

                    try
                    {
                        Task.WaitAll(cleanupTasks.ToArray());
                    }
                    catch (Exception ex)
                    {

                        ReportFailure("Documents cleanup failed", ex);
                    }

                    DocumentStore.Subscriptions.Delete(UsersSubscriptionName);
                    DocumentStore.Subscriptions.Delete(OrdersSubscriptionName);

                    for (int i = 0; i < _shipperGuid.Length; i++)
                    {
                        DocumentStore.Subscriptions.Delete($"shipperSubscription-{i}.{GenralGuid}");
                    }
                }
              
            }
        }

        private string OrdersSubscriptionName => $"CreateOrderSubscription.{GenralGuid}";

        private string UsersSubscriptionName => $"UsersSubscription.{GenralGuid}";        

        private void InsertProducts()
        {
            Guid guid;
            using (var session = DocumentStore.OpenSession())
            {
                for (int i = 0; i < ProductsCount; i++)
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
                for (int i = 0; i < ShippersCount; i++)
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
                for (int i = 1; i <= UsersCount; i++)
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
            
            subscription.Run(batch =>
            {
                using (var session = batch.OpenSession())
                {
                    foreach (var doc in batch.Items)
                    {
                        try
                        {
                            if (doc.Id.StartsWith($"user2.{GenralGuid}") == false)
                            {
                                startsWithSkipCount++;
                                continue;
                            }

                            var randNumber1 = rand.Next(1, 6);

                            doc.Result.Products = new LinkedList<string>();


                            for (var i = 0; i < randNumber1; i++)
                            {
                                var randNumber2 = rand.Next(0, 2);
                                var randNumber3 = rand.Next(0, 4);
                                Debug.Assert(randNumber2 * randNumber3 < ProductsCount);

                                doc.Result.Products.AddFirst($"products.{GenralGuid}/{_productsGuid[randNumber2 * randNumber3]}");
                            }
                            
                            session.Store(doc.Result);                            
                            counter++;
                        }
                        catch (Exception ex)
                        {

                            Console.WriteLine($"ex during insert products: {ex}");
                        }
                    }
                    session.SaveChanges();
                }
            });
        }

        private void CreateOrderDocFromUserProductNames(SubscriptionWorker<dynamic> subscription)
        {            
            var rand = new Random();            
            subscription.Run(batch =>
            {
                using (var session = batch.OpenSession())
                {
                    foreach (var doc in batch.Items)
                    {                      
                        var shipper = rand.Next(0, ShippersCount);
                        var list = new LinkedList<string>();

                        // if we accepted a document that already has products, it means it was processed by the products appending subscription and it's processing progress
                        // can be registered
                        if (doc.Result.ProductsNames == null)
                        {
                            continue;
                        }

                        var x = (doc.Result.ProductsNames as JArray).GetEnumerator();

                        while (x.MoveNext())
                        {
                            list.AddFirst(x.Current.ToString());
                        }

                        var order = new Order
                        {
                            ShipVia = $"shipper.{GenralGuid}/{_shipperGuid[shipper]}",
                            ShipTo = doc.Id,
                            ProductsNames = list
                        };
                        _shipper[shipper] += 1;

                        session.Store(order, $"order.{GenralGuid}/");
                        
                    }
                    session.SaveChanges();

                }
            });
        }        

        private void TrackShipperDataFromOrders(SubscriptionWorker<Order> subscription, int i, CountdownEvent countdown)
        {            
            subscription.Run(async batch =>
            {
                foreach (var doc in batch.Items)
                {
                    if (doc.Id.StartsWith($"order." + GenralGuid) == false)
                        continue;
                    
                    Interlocked.Increment(ref _shipperRes[i]);
                    countdown.Signal();
                }                
            });            
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
            public string Id { get; set; }
            public string Name { get; set; }
            public float PricePerUnit { get; set; }
            public float QuantityPerUnit { get; set; }
        }

        internal class Order
        {
            public string Id { get; set; }
            public string ShipTo { get; set; }
            public LinkedList<string> ProductsNames { get; set; }
            public string ShipVia { get; set; }
        }

        internal class Shipper
        {
            public string Id { get; set; }
            public string Name { get; set; }
        }
    }
}
