using System.Collections.Generic;
using System.Threading;
using Raven.Client.Documents;

namespace AuthorizationBundle
{
    public class Program
    {
        static void Main(string[] args)
        {
            RunAuthorizationBundleScenarios(args[0]);
            /*
            var rootUser = "root";
            var rootPassword = "salamandra#2753";
            var database = "Hospital";
            using (var store = new DocumentStore {Urls = new[] {"http://127.0.0.1:8080"},Database = database }.Initialize())
            {
                //TODO:try and create two root users
                AuthorizedSession.CreateRootUser(store, rootUser, rootPassword);
                using (var session = AuthorizedSession.OpenSession(store, rootUser, rootPassword))
                {
                    session.CreateGroup("Staff", null,"General group for hospital staff", 
                        new Permission { Collections = new HashSet<string> { "StaffStuffs" }, Description = "Permissions for general staff stuff" });
                    session.CreateGroup("Doctors", "Staff","Doctors group",new Permission
                    {
                        Collections = new HashSet<string>{"Prescription"},
                        Description = "Permissions to write prescription to patients"
                    });
                    session.CreateGroup("Cardiology", "Doctors", "Cardiologist only",new Permission
                    {
                        Collections = new HashSet<string> { "EchocardiogramSchedule", "EchocardiogramResults" },
                        Description = "Permissions to schedule an echo exam to patients"
                    });
                    session.CreateGroup("Nurses", "Staff", "Nurses group", new Permission
                    {
                        Collections = new HashSet<string> { "BloodTests" },
                        Description = "Permissions to schedule and read blood tests results"
                    });
                    session.CreateGroup("CardiologyNurses", "Nurses", "Cardiology nurses group", new Permission
                    {
                        Collections = new HashSet<string> { "EchocardiogramResults" },
                        Description = "Permissions to read echo exam results for patients"
                    });
                    session.CreateUser("Shula","Shabazula$23");
                    //session.GiveUserPermissionToAccessDocument("Shula","ShulaEchoResult", "EchocardiogramResults");
                    session.CreateUser("Dr.Shila","fgrh%@34");
                    session.AddUserToGroup("Dr.Shila", "Cardiology");
                    session.CreateUser("Nurse.Judy", "afrgre#$@#28");
                    session.CreateGroup("SecretCardiologyNurses", "CardiologyNurses", "Cardiology nurses secret group", new Permission
                    {
                        Collections = new HashSet<string> { "SecretCardiologyEvents" },
                        Description = "Permissions to create and invite to secret Cardiology events"
                    });
                    session.AddUserToGroup("Nurse.Judy", "SecretCardiologyNurses");
                }

                using (var session = AuthorizedSession.OpenSession(store, "Nurse.Judy", "afrgre#$@#28"))
                {
                    session.GiveUserPermissionToAccessDocument("Shula", "ShulaEchoResult", "EchocardiogramResults");
                }
            }
            */
        }

        private static void RunAuthorizationBundleScenarios(string url)
        {
            using (var client = new HospitalTest(url, "HospitalTest", -1))
            {
                client.Initialize();
                client.RunTest();
            }
        }
    }
}
