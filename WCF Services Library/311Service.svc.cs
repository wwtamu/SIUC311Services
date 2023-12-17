using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;
using System.Security.Cryptography;
using System.Net.Mail;
using System.IO;
using System.Net.Mime;
using System.Net;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Data;
using System.Data.OleDb;
using System.Threading;
using System.Linq.Expressions;

namespace SIUC311Services
{
    public static class SessionInterface
    {
        public static int pageSize = 10;
        public static Object syncLock = new Object();
        public static List<Session> SessionList = new List<Session>();
        public static System.Threading.Timer balanceTimer;
    }

    [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class Service311 : I311Service
    {
        public Service311()
        {
            //StartDatabaseCheckThread();           
        }

        public void StartDatabaseCheckThread()
        {
            // Create the timer callback delegate.
            System.Threading.TimerCallback cb = new System.Threading.TimerCallback(CheckBalance);

            // Create the timer. It is autostart, so creating the timer will start it.
            // Checks functional dependency every hour
            SessionInterface.balanceTimer = new System.Threading.Timer(cb, null, 6000000, Timeout.Infinite);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="obj"></param>
        private void CheckBalance(object obj)
        {
        }

        /// <summary>
        /// Starts a session per app user. Hashes user name into session_id used as an identifier.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public string BeginSession(String name)
        {
            string session_id;
            byte[] byte_hash = new byte[100];
            SHA1 sha1_hasher = new SHA1CryptoServiceProvider();
            byte_hash = sha1_hasher.ComputeHash(System.Text.Encoding.UTF8.GetBytes(name));
            session_id = Convert.ToBase64String(byte_hash);

            Session current_session = new Session();
            current_session.SessionID = session_id;
            current_session.Owner = name;

            lock (SessionInterface.syncLock)
            {
                SessionInterface.SessionList.Add(current_session);
            }

            return session_id;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="session_id"></param>
        /// <returns></returns>
        public bool CheckPermissions(String session_id)
        {
            using (reportsEntities ReportData = new reportsEntities())
            {
                string admin_id;
                byte[] byte_hash = new byte[100];
                SHA1 sha1_hasher = new SHA1CryptoServiceProvider();
                IQueryable<String> admins = from admin in ReportData.Administrators
                                            select admin.radmin;
                foreach (String a in admins)
                {   
                    byte_hash = sha1_hasher.ComputeHash(System.Text.Encoding.UTF8.GetBytes(a));
                    admin_id = Convert.ToBase64String(byte_hash);
                    if (session_id == admin_id)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Returns true if session is in list and false if not found.
        /// </summary>
        /// <param name="session_id"></param>
        /// <returns></returns>
        public bool IsSessionAlive(String session_id)
        {
            foreach (var session in SessionInterface.SessionList)
            {
                if (session.SessionID == session_id)
                {
                    return true;
                }
            }
            return false;
        }

        // NEED TO BE AWARE OF POSSIBILITY OF APP CLOSING WITHOUT ENDING SESSION
        // HANDLE BY SESSION EXPIRATION AND THREAD CHECKING PERIODICALLY

        /// <summary>
        /// Ends a session when the user exits the app. Removes session from list.
        /// </summary>
        /// <param name="session_id"></param>
        /// <returns></returns>
        public bool EndSession(String session_id)
        {
            foreach (var session in SessionInterface.SessionList)
            {
                if (session.SessionID == session_id)
                {
                    lock (SessionInterface.syncLock)
                    {
                        SessionInterface.SessionList.Remove(session);
                    }
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns session for given session_id.
        /// </summary>
        /// <param name="session_id"></param>
        /// <returns></returns>        
        public Session GetSession(String session_id)
        {
            foreach (var session in SessionInterface.SessionList)
            {
                if (session.SessionID == session_id)
                    return session;
            }
            return null;
        }
        
        /// <summary>
        /// Not used yet. Returns session_id from name.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public String GetSessionID(String name)
        {
            string session_id;
            byte[] byte_hash = new byte[100];
            SHA1 sha1_hasher = new SHA1CryptoServiceProvider();
            byte_hash = sha1_hasher.ComputeHash(System.Text.Encoding.UTF8.GetBytes(name));
            session_id = Convert.ToBase64String(byte_hash);
            return session_id;
        }

        /// <summary>
        /// Inserts a report into the reports table. 
        /// Sets corresponding variables in the session and invokes ManageReport.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool InsertReport(String session_id, ReportObject RepInsert)
        {
            Session this_session = GetSession(session_id);
            reportsEntities ReportData = new reportsEntities();
            Report NewReport = new Report();

            NewReport.rtype = RepInsert.ReportType;
            NewReport.rown = RepInsert.ReportAuthor;
            NewReport.rdesc = RepInsert.ReportDescription;
            NewReport.rloc = RepInsert.ReportLocation;
            NewReport.rtime = RepInsert.ReportTime;
            NewReport.rlat = RepInsert.ReportLatitude;
            NewReport.rlon = RepInsert.ReportLongitude;
            NewReport.racc = RepInsert.ReportAccuracy;
            NewReport.rdir = RepInsert.ReportDirection;

            lock (SessionInterface.syncLock)
            {
                ReportData.Reports.Add(NewReport);
                ReportData.SaveChanges();

                RepInsert.ReportId = NewReport.rid;

                this_session.ReportHalf = true;
                this_session.LastReportObject = RepInsert;

                ManageReport(session_id, NewReport);
            }

            return true;
        }

        /// <summary>
        /// Inserts a photo into photos table.
        /// Sets corresponding variables in the session and invokes SendMail.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool InsertPhoto(String session_id, PhotoObject PhoInsert)
        {
            Session this_session = GetSession(session_id);
            reportsEntities PhotoData = new reportsEntities();
            Photo NewPhoto = new Photo();

            NewPhoto.rimage = PhoInsert.ReportPhoto;
            NewPhoto.fid = this_session.LastReportObject.ReportId;

            lock (SessionInterface.syncLock)
            {
                PhotoData.Photos.Add(NewPhoto);
                PhotoData.SaveChanges();

                this_session.PVHalf = true;
                this_session.LastPhotoObject.ReportPhoto = NewPhoto.rimage;

                SendMail(session_id);
            }

            return true;
        }

        // MODIFY TO COUNT FREQUENCY AND ESTABLISH PRIORITY

        /// <summary>
        /// Used to determine status, priority, and frequency. 
        /// Then inserts values into Managements table.
        /// Sets corresponding session variables. Then invokes EnsurePVInsert.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool ManageReport(String session_id, Report Rep)
        {
            reportsEntities ReportData = new reportsEntities();
            Management NewManagement = new Management();

            NewManagement.rstat = "Open";
            NewManagement.rprio = "Normal"; // Elevated, Urgent, 
            NewManagement.rfreq = 1;
            NewManagement.fid = Rep.rid;

            ReportData.Managements.Add(NewManagement);
            ReportData.SaveChanges();

            ReportManagement NewReportManagement = new ReportManagement();
            NewReportManagement.ReportId = NewManagement.rid;
            NewReportManagement.ReportStatus = NewManagement.rstat;
            NewReportManagement.ReportPriority = NewManagement.rprio;
            NewReportManagement.ReportFrequency = NewManagement.rfreq;
            NewReportManagement.ReportForeignKey = NewManagement.fid;

            Session this_session = GetSession(session_id);
            this_session.LastReportManagement = NewReportManagement;

            EnsurePVInsert(session_id);

            return true;
        }

        /// <summary>
        /// Starts a timer and invokes CheckPV when time elapses.
        /// </summary>
        /// <param name="session_id"></param>
        public void EnsurePVInsert(String session_id)
        {
            Session this_session = GetSession(session_id);

            // Create the timer callback delegate.
            System.Threading.TimerCallback cb = new System.Threading.
            TimerCallback(CheckPV);

            tSO ses = new tSO();
            ses.SetSessionID(session_id);

            // Create the timer. It is autostart, so creating the timer will start it.
            // Gives 45 seconds for photo or video to upload
            this_session.Timer = new System.Threading.Timer(cb, ses, 45000, Timeout.Infinite);
        }

        /// <summary>
        /// Checks to see if report submission completes with photo in current session.
        /// If report did not receive a photo it inserts an empty photo.
        /// Then disposes thread.
        /// </summary>
        /// <param name="obj"></param>
        private void CheckPV(object obj)
        {
            Session this_session = GetSession(((tSO)obj).GetSessionID());
            if (this_session != null)
            {
                if (this_session.ReportHalf == true && this_session.PVHalf == false)
                {
                    reportsEntities PhotoData = new reportsEntities();
                    Photo NewPhoto = new Photo();

                    Image img = Bitmap.FromFile(@"C:\Temp\Test\Empty.png");
                    NewPhoto.rimage = imageToByteArray(img);
                    NewPhoto.fid = this_session.LastReportObject.ReportId;

                    PhotoData.Photos.Add(NewPhoto);
                    PhotoData.SaveChanges();
                }
            }
            this_session.Timer.Dispose();
        }

        /// <summary>
        /// Converts image from filepath to byte array.
        /// </summary>
        /// <param name="imageIn"></param>
        /// <returns></returns>
        public byte[] imageToByteArray(System.Drawing.Image imageIn)
        {
            MemoryStream ms = new MemoryStream();
            imageIn.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }

        /// <summary>
        /// Object used to pass to the call back function of the timer thread.
        /// </summary>
        class tSO
        {
            private static String sid;
            public string GetSessionID()
            {
                return sid;
            }
            public void SetSessionID(String s)
            {
                sid = s;
            }
        }

        private static String DetermineRecipient(String report_type)
        {
            if      (report_type == "Pothole")              { return "mobiledawg@siu.edu"; }
            else if (report_type == "Broken parking meter") { return "mobiledawg@siu.edu"; }
            else if (report_type == "Stolen property")      { return "mobiledawg@siu.edu"; }
            else if (report_type == "Stray animal")         { return "mobiledawg@siu.edu"; }
            else if (report_type == "Animal bite")          { return "mobiledawg@siu.edu"; }
            else if (report_type == "Garbage Collection")   { return "mobiledawg@siu.edu"; }
            else if (report_type == "Abandon automobile")   { return "mobiledawg@siu.edu"; }
            else if (report_type == "Abandon property")     { return "mobiledawg@siu.edu"; }
            else if (report_type == "Illegal parking")      { return "mobiledawg@siu.edu"; }
            else if (report_type == "Illegal dumping")      { return "mobiledawg@siu.edu"; }
            else if (report_type == "Street light repair")  { return "mobiledawg@siu.edu"; }
            else if (report_type == "Tree services")        { return "mobiledawg@siu.edu"; }
            else if (report_type == "Plumbing")             { return "mobiledawg@siu.edu"; }
            else if (report_type == "Trash")                { return "mobiledawg@siu.edu"; }
            else if (report_type == "Building damages")     { return "mobiledawg@siu.edu"; }
            else if (report_type == "Graffiti")             { return "mobiledawg@siu.edu"; }
            else if (report_type == "Sidewalk condition")   { return "mobiledawg@siu.edu"; }
            else if (report_type == "Lost property")        { return "mobiledawg@siu.edu"; }
            else if (report_type == "Dangerous weather")    { return "mobiledawg@siu.edu"; }
            else if (report_type == "Other")                { return "mobiledawg@siu.edu"; }
            return "mobiledawg@siu.edu";
        }

        /// <summary>
        /// Sends report and photo by email.
        /// </summary>
        /// <param name="session_id"></param>
        /// <returns></returns>
        public int SendMail(String session_id)
        {
            Session this_session = GetSession(session_id);
            String email_subject = "SIUC 311 Reporting - New Report - " + this_session.LastReportObject.ReportType;
            String email_body = "Submitted by: " + this_session.LastReportObject.ReportAuthor + "\n" +
                                "at:" + this_session.LastReportObject.ReportTime + "\n\n" +
                                this_session.LastReportObject.ReportDescription + "\n\n" +
                                this_session.LastReportObject.ReportLocation + "\n\n" +
                                "Latitude: " + this_session.LastReportObject.ReportLatitude + "\n" +
                                "Longitude: " + this_session.LastReportObject.ReportLongitude + "\n" +
                                "Accuracy: " + this_session.LastReportObject.ReportAccuracy + "\n" +
                                "Direction: " + this_session.LastReportObject.ReportDirection + "\n\n" +
                                "Status: " + this_session.LastReportManagement.ReportStatus + "\n" +
                                "Priority: " + this_session.LastReportManagement.ReportPriority + "\n" +
                                "Frequency: " + this_session.LastReportManagement.ReportFrequency;

            string emailFrom = "mobiledawg@siu.edu";
            string pw = "";
            string emailTo = DetermineRecipient(this_session.LastReportObject.ReportType);

            //string fileAttachmentPath = @"C:\Temp\Test\TextFile.txt";

            Mail mailer = new Mail();
            int result = -1;

            try
            {
                List<FileAttachment> allAttachments = new List<FileAttachment>();
                //FileAttachment attachment = new FileAttachment();
                //attachment.Info = new FileInfo(fileAttachmentPath);
                //attachment.FileContentBase64 = Convert.ToBase64String( File.ReadAllBytes(fileAttachmentPath) );
                //allAttachments.Add(attachment);

                result = mailer.SendEmail(emailFrom, pw, new string[] { emailTo }, null, email_subject, email_body, false, allAttachments.ToArray(), this_session.LastPhotoObject.ReportPhoto);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            this_session.PVHalf = false;
            this_session.ReportHalf = false;

            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="session_id"></param>
        /// <param name="isNew"></param>
        /// <param name="dir"></param>
        /// <param name="paged"></param>
        /// <param name="sort_select"></param>
        /// <returns></returns>
        public List<ReportObject> GetAllReports(String session_id, bool isNew, int dir, bool paged, int[] sort_select)
        {
            var ReportList = new List<ReportObject>();

            Session current_session = GetSession(session_id);

            if (isNew)
            {
                using (reportsEntities ReportData = new reportsEntities())
                {
                    IOrderedQueryable<Report> reports;

                    switch (sort_select[0])
                    {
                        case 0:  // 0
                            {
                                switch (sort_select[1])
                                {
                                    case 0: // 00
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 000
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 0000
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                            case 1: // 0001
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY m.rstat, r.rtime DESC").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 1: // 001
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 0010
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rown, report.rtime descending
                                                                              select report;
                                                                } break;
                                                            case 2: // 0012
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rown, m.rstat, r.rtime DESC").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 2: // 002
                                                    {
                                                        // 0021
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY m.rstat, r.rown, r.rtime DESC").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 1: // 01
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 010
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 0100
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtime
                                                                              select report;
                                                                } break;
                                                            case 2: // 0102
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 2: // 012
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 0120
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtime, report.rown
                                                                              select report;
                                                                } break;
                                                            case 3: // 0123
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rtime, r.rown, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 3: // 013
                                                    {
                                                        // 0132
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rtime, m.rstat, r.rown").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 2: // 02
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 020
                                                    {
                                                        // 0201
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 1: // 021
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 0210
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rown, report.rtime
                                                                              select report;
                                                                } break;
                                                            case 3: // 0213
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rown, r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 3: // 023
                                                    {
                                                        // 0231
                                                        reports = from report in ReportData.Reports
                                                                  orderby report.rtime, report.rown
                                                                  select report;
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 3:
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 1: // 031
                                                    {
                                                        // 0312
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rown, m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 2: // 032
                                                    {
                                                        // 0321
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY m.rstat, r.rown, r.rtime").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    default:
                                        {
                                            reports = from report in ReportData.Reports
                                                      orderby report.rtime descending
                                                      select report;
                                        } break;
                                }
                            } break;
                        case 1: // 1
                            {
                                switch (sort_select[1])
                                {
                                    case 0: // 10
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 100
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 1000
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtype, report.rtime descending
                                                                              select report;
                                                                } break;
                                                            case 2: // 1002
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rtype, m.rstat, r.rtime DESC").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 2: // 102
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 1020
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtype, report.rown, report.rtime descending
                                                                              select report;
                                                                } break;
                                                            case 3: // 1023
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rtype, r.rown, m.rstat, r.rtime DESC").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 3: // 103
                                                    {
                                                        // 1032
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rtype, m.rstat, r.rown, r.rtime DESC").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 2: // 12
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 120
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 1200
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtype, report.rtime
                                                                              select report;
                                                                } break;
                                                            case 3: // 1203
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rtype, r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 3: // 123
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 1230
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtype, report.rtime, report.rown
                                                                              select report;
                                                                } break;
                                                            case 4: // 1234
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rtype, r.rtime, r.rown, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 4: // 124
                                                    {
                                                        // 1243
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rtype, r.rtime, m.rstat, r.rown").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 3: // 13
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 130
                                                    {
                                                        // 1302
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rtype, m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 2: // 132
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 1320
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtype, report.rown, report.rtime
                                                                              select report;
                                                                } break;
                                                            case 4: // 1324
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rtype, r.rown, r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 4: // 134
                                                    {
                                                        // 1342
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rtype, m.rstat, r.rtime, r.rown").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 4: // 14
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 2: // 142
                                                    {
                                                        // 1423
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rtype, r.rown, m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 3: // 143
                                                    {
                                                        // 1432
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rtype, m.rstat, r.rown, r.rtime").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    default:
                                        {
                                            reports = from report in ReportData.Reports
                                                      orderby report.rtime descending
                                                      select report;
                                        } break;
                                }
                            } break;
                        case 2: // 2
                            {
                                switch (sort_select[1])
                                {
                                    case 0: // 20
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 200
                                                    {
                                                        // 2001
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY m.rstat, r.rtype, r.rtime DESC").AsQueryable();
                                                    } break;
                                                case 1: // 201
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 2010
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rown, report.rtype, report.rtime descending
                                                                              select report;
                                                                } break;
                                                            case 3: // 2013 // NO SORT ON STATUS YET
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rown, r.rtype, m.rstat, r.rtime DESC").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 3: // 203
                                                    {
                                                        // 2031
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY m.rstat, r.rtype, r.rown, r.rtime DESC").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 1: // 21
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 210
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 2100
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtime, report.rtype
                                                                              select report;
                                                                } break;
                                                            case 3: // 2103
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rtime, r.rtype, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 3: // 213
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 2130
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtime, report.rtype, report.rown
                                                                              select report;
                                                                } break;
                                                            case 4: // 2134
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rtime, r.rtype, r.rown, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 4: // 214
                                                    {
                                                        // 2143
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rtime, r.rtype, m.rstat, r.rown").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 3: // 23
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 230
                                                    {
                                                        // 2301
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY m.rstat, r.rtype, r.rtime").AsQueryable();
                                                    } break;
                                                case 1: // 231
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 2310
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rown, report.rtype, report.rtime
                                                                              select report;
                                                                } break;
                                                            case 4: // 2314
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rown, r.rtype, r.rown, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 4: // 234
                                                    {
                                                        // 2341
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY m.rstat, r.rtype, r.rtime, r.rown").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 4: // 24
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 1: // 241
                                                    {
                                                        // 2413 
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rown, r.rtype, m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 3: // 243
                                                    {
                                                        // 2431
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY m.rstat, r.rtype, r.rown, r.rtime").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    default:
                                        {
                                            reports = from report in ReportData.Reports
                                                      orderby report.rtime descending
                                                      select report;
                                        } break;
                                }
                            } break;
                        case 3: // 3
                            {
                                switch (sort_select[1])
                                {
                                    case 0: // 30
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 1: // 301
                                                    {
                                                        // 3012
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rown, m.rstat, r.rtype, r.rtime DESC").AsQueryable();
                                                    } break;
                                                case 2: // 302
                                                    {
                                                        // 3021
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY m.rstat, r.rown, r.rtype, r.rtime DESC").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 1: // 31
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 310
                                                    {
                                                        // 3102
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rtime, m.rstat, r.rtype").AsQueryable();
                                                    } break;
                                                case 2: // 312
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 3120
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtime, report.rown, report.rtype
                                                                              select report;
                                                                } break;
                                                            case 4: // 3124
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rtime, r.rown, r.rtype, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 4: // 314
                                                    {
                                                        // 3142
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rtime, m.rstat, r.rtype, r.rown").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 2: // 32
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 320
                                                    {
                                                        // 3201
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY m.rstat, r.rtime, r.rtype").AsQueryable();
                                                    } break;
                                                case 1: // 321
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 3210
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rown, report.rtime, report.rtype
                                                                              select report;
                                                                } break;
                                                            case 4: // 3214
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rown, r.rtime, r.rtype, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 4: // 324
                                                    {
                                                        // 3241
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY m.rstat, r.rtime, r.rtype, r.rown").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 4: // 34
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 1: // 341
                                                    {
                                                        // 3412
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rown, m.rstat, r.rtype, r.rtime").AsQueryable();
                                                    } break;
                                                case 2: // 342
                                                    {
                                                        // 3421
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY m.rstat, r.rown, r.rtype, r.rtime").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    default:
                                        {
                                            reports = from report in ReportData.Reports
                                                      orderby report.rtime descending
                                                      select report;
                                        } break;
                                }
                            } break;
                        case 4: // 4
                            {

                                switch (sort_select[1])
                                {
                                    case 1: // 41
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 2: // 412
                                                    {
                                                        // 4123
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rtime, r.rown, m.rstat, r.rtype").AsQueryable();
                                                    } break;
                                                case 3: // 413
                                                    {
                                                        // 4132
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rtime, m.rstat, r.rown, r.rtype").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 2: // 42
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 1: // 421
                                                    {
                                                        // 4213
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rown, r.rtime, m.rstat, r.rtype").AsQueryable();
                                                    } break;
                                                case 3: // 423
                                                    {
                                                        // 4231
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY m.rstat, r.rtime, r.rown, r.rtype").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 3: // 43
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 1: // 431
                                                    {
                                                        // 4312
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY r.rown, m.rstat, r.rtime, r.rtype").AsQueryable();
                                                    } break;
                                                case 2: // 432
                                                    {
                                                        // 4321
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           ORDER BY m.rstat, r.rown, r.rtime, r.rtype").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    default:
                                        {
                                            reports = from report in ReportData.Reports
                                                      orderby report.rtime descending
                                                      select report;
                                        } break;
                                }
                            } break;
                        default:
                            {
                                reports = from report in ReportData.Reports
                                          orderby report.rtime descending
                                          select report;
                            } break;
                    }

                    lock (SessionInterface.syncLock)
                    {
                        if (!paged)
                        {
                            current_session.SessionPage.QPage = 0;
                        }
                        current_session.SessionPage.QCount = reports.Count();
                    }

                    var FullReportList = new List<ReportObject>();

                    foreach (var Rep in reports.ToList())
                    {
                        ReportObject GetReport = new ReportObject();

                        GetReport.ReportId = Rep.rid;
                        GetReport.ReportType = Rep.rtype;
                        GetReport.ReportAuthor = Rep.rown;
                        GetReport.ReportDescription = Rep.rdesc;
                        GetReport.ReportLocation = Rep.rloc;
                        GetReport.ReportTime = Rep.rtime;
                        GetReport.ReportLatitude = Rep.rlat;
                        GetReport.ReportLongitude = Rep.rlon;
                        GetReport.ReportAccuracy = Rep.racc;
                        GetReport.ReportDirection = Rep.rdir;

                        FullReportList.Add(GetReport);
                    }

                    lock (SessionInterface.syncLock)
                    {
                        current_session.SessionPage.QTotal = FullReportList.Count;
                        current_session.SessionReportList = FullReportList;
                    }

                    if (paged)
                    {
                        foreach (var Rep in current_session.SessionReportList.Skip(current_session.SessionPage.QPage * SessionInterface.pageSize).Take(SessionInterface.pageSize))
                        {
                            ReportObject GetReport = new ReportObject();

                            GetReport.ReportId = Rep.ReportId;
                            GetReport.ReportType = Rep.ReportType;
                            GetReport.ReportAuthor = Rep.ReportAuthor;
                            GetReport.ReportDescription = Rep.ReportDescription;
                            GetReport.ReportLocation = Rep.ReportLocation;
                            GetReport.ReportTime = Rep.ReportTime;
                            GetReport.ReportLatitude = Rep.ReportLatitude;
                            GetReport.ReportLongitude = Rep.ReportLongitude;
                            GetReport.ReportAccuracy = Rep.ReportAccuracy;
                            GetReport.ReportDirection = Rep.ReportDirection;

                            ReportList.Add(GetReport);
                        }
                    }
                    else
                    {
                        foreach (var Rep in FullReportList.Take(SessionInterface.pageSize))
                        {
                            ReportObject GetReport = new ReportObject();

                            GetReport.ReportId = Rep.ReportId;
                            GetReport.ReportType = Rep.ReportType;
                            GetReport.ReportAuthor = Rep.ReportAuthor;
                            GetReport.ReportDescription = Rep.ReportDescription;
                            GetReport.ReportLocation = Rep.ReportLocation;
                            GetReport.ReportTime = Rep.ReportTime;
                            GetReport.ReportLatitude = Rep.ReportLatitude;
                            GetReport.ReportLongitude = Rep.ReportLongitude;
                            GetReport.ReportAccuracy = Rep.ReportAccuracy;
                            GetReport.ReportDirection = Rep.ReportDirection;

                            ReportList.Add(GetReport);
                        }
                    }
                }
            }
            else
            {
                switch (dir)
                {
                    case 1:
                        {
                            if (current_session.SessionPage.QCount > (SessionInterface.pageSize * (current_session.SessionPage.QPage + 1)))
                            {
                                lock (SessionInterface.syncLock)
                                {
                                    current_session.SessionPage.QPage++;
                                }
                            }
                        } break;
                    case -1:
                        {
                            if (current_session.SessionPage.QPage > 0)
                            {
                                lock (SessionInterface.syncLock)
                                {
                                    current_session.SessionPage.QPage--;
                                }
                            }
                        } break;
                    default: break;
                }

                foreach (var Rep in current_session.SessionReportList.Skip(current_session.SessionPage.QPage * SessionInterface.pageSize).Take(SessionInterface.pageSize))
                {
                    ReportObject GetReport = new ReportObject();

                    GetReport.ReportId = Rep.ReportId;
                    GetReport.ReportType = Rep.ReportType;
                    GetReport.ReportAuthor = Rep.ReportAuthor;
                    GetReport.ReportDescription = Rep.ReportDescription;
                    GetReport.ReportLocation = Rep.ReportLocation;
                    GetReport.ReportTime = Rep.ReportTime;
                    GetReport.ReportLatitude = Rep.ReportLatitude;
                    GetReport.ReportLongitude = Rep.ReportLongitude;
                    GetReport.ReportAccuracy = Rep.ReportAccuracy;
                    GetReport.ReportDirection = Rep.ReportDirection;

                    ReportList.Add(GetReport);
                }
            }
            return ReportList;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="session_id"></param>
        /// <param name="isNew"></param>
        /// <param name="dir"></param>
        /// <param name="paged"></param>
        /// <param name="sort_select"></param>
        /// <returns></returns>
        public List<ReportObject> GetMyReports(String session_id, bool isNew, int dir, bool paged, int[] sort_select)
        {
            var ReportList = new List<ReportObject>();

            Session current_session = GetSession(session_id);

            if (isNew)
            {
                using (reportsEntities ReportData = new reportsEntities())
                {   
                    IOrderedQueryable<Report> reports;

                    switch (sort_select[0])
                    {
                        case 0:  // 0
                            {
                                switch (sort_select[1])
                                {
                                    case 0: // 00
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 000
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 0000
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                            case 1: // 0001
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r                                                                                                           
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + 
                                                                                                           "' ORDER BY m.rstat, r.rtime DESC").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 1: // 001
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 0010
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                            case 2: // 0012
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r                                                                                                           
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY m.rstat, r.rtime DESC").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 2: // 002
                                                    {
                                                        // 0021
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r                                                                                                           
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY m.rstat, r.rtime DESC").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 1: // 01
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 010
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 0100
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtime
                                                                              select report;
                                                                } break;
                                                            case 2: // 0102
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r                                                                                                           
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 2: // 012
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 0120
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtime
                                                                              select report;
                                                                } break;
                                                            case 3: // 0123
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 3: // 013
                                                    {
                                                        // 0132
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 2: // 02
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 020
                                                    {
                                                        // 0201
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 1: // 021
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 0210
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtime
                                                                              select report;
                                                                } break;
                                                            case 3: // 0213
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 3: // 023
                                                    {
                                                        // 0231
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner
                                                                  orderby report.rtime, report.rown
                                                                  select report;
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 3:
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 1: // 031
                                                    {
                                                        // 0312
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 2: // 032
                                                    {
                                                        // 0321
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    default:
                                        {
                                            reports = from report in ReportData.Reports
                                                      where report.rown == current_session.Owner
                                                      orderby report.rtime descending
                                                      select report;
                                        } break;
                                }
                            } break;
                        case 1: // 1
                            {
                                switch (sort_select[1])
                                {
                                    case 0: // 10
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 100
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 1000
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtype, report.rtime descending
                                                                              select report;
                                                                } break;
                                                            case 2: // 1002
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtype, m.rstat, r.rtime DESC").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 2: // 102
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 1020
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtype, report.rtime descending
                                                                              select report;
                                                                } break;
                                                            case 3: // 1023
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtype, m.rstat, r.rtime DESC").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 3: // 103
                                                    {
                                                        // 1032
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtype, m.rstat, r.rtime DESC").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 2: // 12
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 120
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 1200
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtype, report.rtime
                                                                              select report;
                                                                } break;
                                                            case 3: // 1203
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtype, r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 3: // 123
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 1230
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtype, report.rtime
                                                                              select report;
                                                                } break;
                                                            case 4: // 1234
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtype, r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 4: // 124
                                                    {
                                                        // 1243
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtype, r.rtime, m.rstat").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 3: // 13
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 130
                                                    {
                                                        // 1302
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtype, m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 2: // 132
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 1320
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtype, report.rtime
                                                                              select report;
                                                                } break;
                                                            case 4: // 1324
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtype, r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 4: // 134
                                                    {
                                                        // 1342
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtype, m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 4: // 14
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 2: // 142
                                                    {
                                                        // 1423
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtype, m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 3: // 143
                                                    {
                                                        // 1432
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtype, m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    default:
                                        {
                                            reports = from report in ReportData.Reports
                                                      where report.rown == current_session.Owner
                                                      orderby report.rtime descending
                                                      select report;
                                        } break;
                                }
                            } break;
                        case 2: // 2
                            {
                                switch (sort_select[1])
                                {
                                    case 0: // 20
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 200
                                                    {
                                                        // 2001
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY m.rstat, r.rtype, r.rtime DESC").AsQueryable();
                                                    } break;
                                                case 1: // 201
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 2010
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtype, report.rtime descending
                                                                              select report;
                                                                } break;
                                                            case 3: // 2013 // NO SORT ON STATUS YET
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtype, m.rstat, r.rtime DESC").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 3: // 203
                                                    {
                                                        // 2031
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY m.rstat, r.rtype, r.rtime DESC").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 1: // 21
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 210
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 2100
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtime, report.rtype
                                                                              select report;
                                                                } break;
                                                            case 3: // 2103
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtime, r.rtype, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 3: // 213
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 2130
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtime, report.rtype
                                                                              select report;
                                                                } break;
                                                            case 4: // 2134
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtime, r.rtype, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 4: // 214
                                                    {
                                                        // 2143
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtime, r.rtype, m.rstat").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 3: // 23
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 230
                                                    {
                                                        // 2301
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY m.rstat, r.rtype, r.rtime").AsQueryable();
                                                    } break;
                                                case 1: // 231
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 2310
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtype, report.rtime
                                                                              select report;
                                                                } break;
                                                            case 4: // 2314
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rown, r.rtype, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 4: // 234
                                                    {
                                                        // 2341
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY m.rstat, r.rtype, r.rtime").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 4: // 24
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 1: // 241
                                                    {
                                                        // 2413 
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtype, m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 3: // 243
                                                    {
                                                        // 2431
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY m.rstat, r.rtype, r.rtime").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    default:
                                        {
                                            reports = from report in ReportData.Reports
                                                      where report.rown == current_session.Owner
                                                      orderby report.rtime descending
                                                      select report;
                                        } break;
                                }
                            } break;
                        case 3: // 3
                            {
                                switch (sort_select[1])
                                {
                                    case 0: // 30
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 1: // 301
                                                    {
                                                        // 3012
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY m.rstat, r.rtype, r.rtime DESC").AsQueryable();
                                                    } break;
                                                case 2: // 302
                                                    {
                                                        // 3021
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY m.rstat, r.rtype, r.rtime DESC").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 1: // 31
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 310
                                                    {
                                                        // 3102
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtime, m.rstat, r.rtype").AsQueryable();
                                                    } break;
                                                case 2: // 312
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 3120
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtime, report.rtype
                                                                              select report;
                                                                } break;
                                                            case 4: // 3124
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtime, r.rtype, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 4: // 314
                                                    {
                                                        // 3142
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtime, m.rstat, r.rtype").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 2: // 32
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 320
                                                    {
                                                        // 3201
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY m.rstat, r.rtime, r.rtype").AsQueryable();
                                                    } break;
                                                case 1: // 321
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 3210
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtime, report.rtype
                                                                              select report;
                                                                } break;
                                                            case 4: // 3214
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtime, r.rtype, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 4: // 324
                                                    {
                                                        // 3241
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY m.rstat, r.rtime, r.rtype").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 4: // 34
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 1: // 341
                                                    {
                                                        // 3412
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY m.rstat, r.rtype, r.rtime").AsQueryable();
                                                    } break;
                                                case 2: // 342
                                                    {
                                                        // 3421
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY m.rstat, r.rtype, r.rtime").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    default:
                                        {
                                            reports = from report in ReportData.Reports
                                                      where report.rown == current_session.Owner
                                                      orderby report.rtime descending
                                                      select report;
                                        } break;
                                }
                            } break;
                        case 4: // 4
                            {

                                switch (sort_select[1])
                                {
                                    case 1: // 41
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 2: // 412
                                                    {
                                                        // 4123
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtime, m.rstat, r.rtype").AsQueryable();
                                                    } break;
                                                case 3: // 413
                                                    {
                                                        // 4132
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtime, m.rstat, r.rtype").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 2: // 42
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 1: // 421
                                                    {
                                                        // 4213
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY r.rtime, m.rstat, r.rtype").AsQueryable();
                                                    } break;
                                                case 3: // 423
                                                    {
                                                        // 4231
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY m.rstat, r.rtime, r.rtype").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 3: // 43
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 1: // 431
                                                    {
                                                        // 4312
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY m.rstat, r.rtime, r.rtype").AsQueryable();
                                                    } break;
                                                case 2: // 432
                                                    {
                                                        // 4321
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner +
                                                                                                           "' ORDER BY m.rstat, r.rtime, r.rtype").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    default:
                                        {
                                            reports = from report in ReportData.Reports
                                                      where report.rown == current_session.Owner
                                                      orderby report.rtime descending
                                                      select report;
                                        } break;
                                }
                            } break;
                        default:
                            {
                                reports = from report in ReportData.Reports
                                          where report.rown == current_session.Owner
                                          orderby report.rtime descending
                                          select report;
                            } break;
                    }

                    lock (SessionInterface.syncLock)
                    {
                        if (!paged)
                        {
                            current_session.SessionPage.QPage = 0;
                        }
                        current_session.SessionPage.QCount = reports.Count();
                    }

                    var FullReportList = new List<ReportObject>();

                    foreach (var Rep in reports.ToList())
                    {
                        ReportObject GetReport = new ReportObject();

                        GetReport.ReportId = Rep.rid;
                        GetReport.ReportType = Rep.rtype;
                        GetReport.ReportAuthor = Rep.rown;
                        GetReport.ReportDescription = Rep.rdesc;
                        GetReport.ReportLocation = Rep.rloc;
                        GetReport.ReportTime = Rep.rtime;
                        GetReport.ReportLatitude = Rep.rlat;
                        GetReport.ReportLongitude = Rep.rlon;
                        GetReport.ReportAccuracy = Rep.racc;
                        GetReport.ReportDirection = Rep.rdir;

                        FullReportList.Add(GetReport);
                    }

                    lock (SessionInterface.syncLock)
                    {
                        current_session.SessionPage.QTotal = FullReportList.Count;
                        current_session.SessionReportList = FullReportList;
                    }

                    if (paged)
                    {
                        foreach (var Rep in current_session.SessionReportList.Skip(current_session.SessionPage.QPage * SessionInterface.pageSize).Take(SessionInterface.pageSize))
                        {
                            ReportObject GetReport = new ReportObject();

                            GetReport.ReportId = Rep.ReportId;
                            GetReport.ReportType = Rep.ReportType;
                            GetReport.ReportAuthor = Rep.ReportAuthor;
                            GetReport.ReportDescription = Rep.ReportDescription;
                            GetReport.ReportLocation = Rep.ReportLocation;
                            GetReport.ReportTime = Rep.ReportTime;
                            GetReport.ReportLatitude = Rep.ReportLatitude;
                            GetReport.ReportLongitude = Rep.ReportLongitude;
                            GetReport.ReportAccuracy = Rep.ReportAccuracy;
                            GetReport.ReportDirection = Rep.ReportDirection;

                            ReportList.Add(GetReport);
                        }
                    }
                    else
                    {
                        foreach (var Rep in FullReportList.Take(SessionInterface.pageSize))
                        {
                            ReportObject GetReport = new ReportObject();

                            GetReport.ReportId = Rep.ReportId;
                            GetReport.ReportType = Rep.ReportType;
                            GetReport.ReportAuthor = Rep.ReportAuthor;
                            GetReport.ReportDescription = Rep.ReportDescription;
                            GetReport.ReportLocation = Rep.ReportLocation;
                            GetReport.ReportTime = Rep.ReportTime;
                            GetReport.ReportLatitude = Rep.ReportLatitude;
                            GetReport.ReportLongitude = Rep.ReportLongitude;
                            GetReport.ReportAccuracy = Rep.ReportAccuracy;
                            GetReport.ReportDirection = Rep.ReportDirection;

                            ReportList.Add(GetReport);
                        }
                    }
                }
            }
            else
            {
                switch (dir)
                {
                    case 1:
                        {
                            if (current_session.SessionPage.QCount > (SessionInterface.pageSize * (current_session.SessionPage.QPage + 1)))
                            {
                                lock (SessionInterface.syncLock)
                                {
                                    current_session.SessionPage.QPage++;
                                }
                            }
                        } break;
                    case -1:
                        {
                            if (current_session.SessionPage.QPage > 0)
                            {
                                lock (SessionInterface.syncLock)
                                {
                                    current_session.SessionPage.QPage--;
                                }
                            }
                        } break;
                    default: break;
                }

                foreach (var Rep in current_session.SessionReportList.Skip(current_session.SessionPage.QPage * SessionInterface.pageSize).Take(SessionInterface.pageSize))
                {
                    ReportObject GetReport = new ReportObject();

                    GetReport.ReportId = Rep.ReportId;
                    GetReport.ReportType = Rep.ReportType;
                    GetReport.ReportAuthor = Rep.ReportAuthor;
                    GetReport.ReportDescription = Rep.ReportDescription;
                    GetReport.ReportLocation = Rep.ReportLocation;
                    GetReport.ReportTime = Rep.ReportTime;
                    GetReport.ReportLatitude = Rep.ReportLatitude;
                    GetReport.ReportLongitude = Rep.ReportLongitude;
                    GetReport.ReportAccuracy = Rep.ReportAccuracy;
                    GetReport.ReportDirection = Rep.ReportDirection;

                    ReportList.Add(GetReport);
                }
            }
            return ReportList;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="type"></param>
        /// <param name="session_id"></param>
        /// <param name="isNew"></param>
        /// <param name="dir"></param>
        /// <param name="paged"></param>
        /// <param name="sort_select"></param>
        /// <returns></returns>
        public List<ReportObject> GetAllReportsByType(String type, String session_id, bool isNew, int dir, bool paged, int[] sort_select)
        {
            var ReportList = new List<ReportObject>();

            Session current_session = GetSession(session_id);

            if (isNew)
            {
                using (reportsEntities ReportData = new reportsEntities())
                {   
                    IOrderedQueryable<Report> reports;

                    switch (sort_select[0])
                    {
                        case 0:  // 0
                            {
                                switch (sort_select[1])
                                {
                                    case 0: // 00
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 000
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 0000
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                            case 1: // 0001
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime DESC").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 1: // 001
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 0010
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rown, report.rtime descending
                                                                              select report;
                                                                } break;
                                                            case 2: // 0012
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rown, m.rstat, r.rtime DESC").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 2: // 002
                                                    {
                                                        // 0021
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rown, r.rtime DESC").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 1: // 01
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 010
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 0100
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rtime
                                                                              select report;
                                                                } break;
                                                            case 2: // 0102
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 2: // 012
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 0120
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rtime, report.rown
                                                                              select report;
                                                                } break;
                                                            case 3: // 0123
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, r.rown, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 3: // 013
                                                    {
                                                        // 0132
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat, r.rown").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 2: // 02
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 020
                                                    {
                                                        // 0201
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 1: // 021
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 0210
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rown, report.rtime
                                                                              select report;
                                                                } break;
                                                            case 3: // 0213
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rown, r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 3: // 023
                                                    {
                                                        // 0231
                                                        reports = from report in ReportData.Reports
                                                                  where report.rtype == type
                                                                  orderby report.rtime, report.rown
                                                                  select report;
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 3:
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 1: // 031
                                                    {
                                                        // 0312
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rown, m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 2: // 032
                                                    {
                                                        // 0321
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rown, r.rtime").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    default:
                                        {
                                            reports = from report in ReportData.Reports
                                                      where report.rtype == type
                                                      orderby report.rtime descending
                                                      select report;
                                        } break;
                                }
                            } break;
                        case 1: // 1
                            {
                                switch (sort_select[1])
                                {
                                    case 0: // 10
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 100
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 1000
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                            case 2: // 1002
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime DESC").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 2: // 102
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 1020
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rown, report.rtime descending
                                                                              select report;
                                                                } break;
                                                            case 3: // 1023
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rown, m.rstat, r.rtime DESC").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 3: // 103
                                                    {
                                                        // 1032
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rown, r.rtime DESC").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 2: // 12
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 120
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 1200
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rtime
                                                                              select report;
                                                                } break;
                                                            case 3: // 1203
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 3: // 123
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 1230
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rtime, report.rown
                                                                              select report;
                                                                } break;
                                                            case 4: // 1234
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, r.rown, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 4: // 124
                                                    {
                                                        // 1243
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat, r.rown").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 3: // 13
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 130
                                                    {
                                                        // 1302
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 2: // 132
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 1320
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rown, report.rtime
                                                                              select report;
                                                                } break;
                                                            case 4: // 1324
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rown, r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 4: // 134
                                                    {
                                                        // 1342
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime, r.rown").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 4: // 14
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 2: // 142
                                                    {
                                                        // 1423
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rown, m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 3: // 143
                                                    {
                                                        // 1432
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rown, r.rtime").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    default:
                                        {
                                            reports = from report in ReportData.Reports
                                                      where report.rtype == type
                                                      orderby report.rtime descending
                                                      select report;
                                        } break;
                                }
                            } break;
                        case 2: // 2
                            {
                                switch (sort_select[1])
                                {
                                    case 0: // 20
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 200
                                                    {
                                                        // 2001
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           " ORDER BY m.rstat, r.rtime DESC").AsQueryable();
                                                    } break;
                                                case 1: // 201
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 2010
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rown, report.rtime descending
                                                                              select report;
                                                                } break;
                                                            case 3: // 2013 // NO SORT ON STATUS YET
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rown, m.rstat, r.rtime DESC").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 3: // 203
                                                    {
                                                        // 2031
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat,r.rown, r.rtime DESC").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 1: // 21
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 210
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 2100
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rtime
                                                                              select report;
                                                                } break;
                                                            case 3: // 2103
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 3: // 213
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 2130
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rtime, report.rown
                                                                              select report;
                                                                } break;
                                                            case 4: // 2134
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, r.rown, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 4: // 214
                                                    {
                                                        // 2143
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat, r.rown").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 3: // 23
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 230
                                                    {
                                                        // 2301
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 1: // 231
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 2310
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rown, report.rtime
                                                                              select report;
                                                                } break;
                                                            case 4: // 2314
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rown, r.rown, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 4: // 234
                                                    {
                                                        // 2341
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime, r.rown").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 4: // 24
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 1: // 241
                                                    {
                                                        // 2413 
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rown, m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 3: // 243
                                                    {
                                                        // 2431
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rown, r.rtime").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    default:
                                        {
                                            reports = from report in ReportData.Reports
                                                      where report.rtype == type
                                                      orderby report.rtime descending
                                                      select report;
                                        } break;
                                }
                            } break;
                        case 3: // 3
                            {
                                switch (sort_select[1])
                                {
                                    case 0: // 30
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 1: // 301
                                                    {
                                                        // 3012
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rown, m.rstat, r.rtime DESC").AsQueryable();
                                                    } break;
                                                case 2: // 302
                                                    {
                                                        // 3021
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rown, r.rtime DESC").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 1: // 31
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 310
                                                    {
                                                        // 3102
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                    } break;
                                                case 2: // 312
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 3120
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rtime, report.rown
                                                                              select report;
                                                                } break;
                                                            case 4: // 3124
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, r.rown, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 4: // 314
                                                    {
                                                        // 3142
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat, r.rown").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 2: // 32
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 320
                                                    {
                                                        // 3201
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 1: // 321
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 3210
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rown, report.rtime
                                                                              select report;
                                                                } break;
                                                            case 4: // 3214
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rown, r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 4: // 324
                                                    {
                                                        // 3241
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime, r.rown").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 4: // 34
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 1: // 341
                                                    {
                                                        // 3412
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rown, m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 2: // 342
                                                    {
                                                        // 3421
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rown, r.rtime").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    default:
                                        {
                                            reports = from report in ReportData.Reports
                                                      where report.rtype == type
                                                      orderby report.rtime descending
                                                      select report;
                                        } break;
                                }
                            } break;
                        case 4: // 4
                            {

                                switch (sort_select[1])
                                {
                                    case 1: // 41
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 2: // 412
                                                    {
                                                        // 4123
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, r.rown, m.rstat").AsQueryable();
                                                    } break;
                                                case 3: // 413
                                                    {
                                                        // 4132
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat, r.rown").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 2: // 42
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 1: // 421
                                                    {
                                                        // 4213
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rown, r.rtime, m.rstat").AsQueryable();
                                                    } break;
                                                case 3: // 423
                                                    {
                                                        // 4231
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime, r.rown").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 3: // 43
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 1: // 431
                                                    {
                                                        // 4312
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rown, m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 2: // 432
                                                    {
                                                        // 4321
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rown, r.rtime").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    default:
                                        {
                                            reports = from report in ReportData.Reports
                                                      where report.rtype == type
                                                      orderby report.rtime descending
                                                      select report;
                                        } break;
                                }
                            } break;
                        default:
                            {
                                reports = from report in ReportData.Reports
                                          where report.rtype == type
                                          orderby report.rtime descending
                                          select report;
                            } break;
                    }

                    lock (SessionInterface.syncLock)
                    {
                        if (!paged)
                        {
                            current_session.SessionPage.QPage = 0;
                        }
                        current_session.SessionPage.QCount = reports.Count();
                    }

                    var FullReportList = new List<ReportObject>();

                    foreach (var Rep in reports.ToList())
                    {
                        ReportObject GetReport = new ReportObject();

                        GetReport.ReportId = Rep.rid;
                        GetReport.ReportType = Rep.rtype;
                        GetReport.ReportAuthor = Rep.rown;
                        GetReport.ReportDescription = Rep.rdesc;
                        GetReport.ReportLocation = Rep.rloc;
                        GetReport.ReportTime = Rep.rtime;
                        GetReport.ReportLatitude = Rep.rlat;
                        GetReport.ReportLongitude = Rep.rlon;
                        GetReport.ReportAccuracy = Rep.racc;
                        GetReport.ReportDirection = Rep.rdir;

                        FullReportList.Add(GetReport);
                    }

                    lock (SessionInterface.syncLock)
                    {
                        current_session.SessionPage.QTotal = FullReportList.Count;
                        current_session.SessionReportList = FullReportList;
                    }

                    if (paged)
                    {
                        foreach (var Rep in current_session.SessionReportList.Skip(current_session.SessionPage.QPage * SessionInterface.pageSize).Take(SessionInterface.pageSize))
                        {
                            ReportObject GetReport = new ReportObject();

                            GetReport.ReportId = Rep.ReportId;
                            GetReport.ReportType = Rep.ReportType;
                            GetReport.ReportAuthor = Rep.ReportAuthor;
                            GetReport.ReportDescription = Rep.ReportDescription;
                            GetReport.ReportLocation = Rep.ReportLocation;
                            GetReport.ReportTime = Rep.ReportTime;
                            GetReport.ReportLatitude = Rep.ReportLatitude;
                            GetReport.ReportLongitude = Rep.ReportLongitude;
                            GetReport.ReportAccuracy = Rep.ReportAccuracy;
                            GetReport.ReportDirection = Rep.ReportDirection;

                            ReportList.Add(GetReport);
                        }
                    }
                    else
                    {
                        foreach (var Rep in FullReportList.Take(SessionInterface.pageSize))
                        {
                            ReportObject GetReport = new ReportObject();

                            GetReport.ReportId = Rep.ReportId;
                            GetReport.ReportType = Rep.ReportType;
                            GetReport.ReportAuthor = Rep.ReportAuthor;
                            GetReport.ReportDescription = Rep.ReportDescription;
                            GetReport.ReportLocation = Rep.ReportLocation;
                            GetReport.ReportTime = Rep.ReportTime;
                            GetReport.ReportLatitude = Rep.ReportLatitude;
                            GetReport.ReportLongitude = Rep.ReportLongitude;
                            GetReport.ReportAccuracy = Rep.ReportAccuracy;
                            GetReport.ReportDirection = Rep.ReportDirection;

                            ReportList.Add(GetReport);
                        }
                    }
                }
            }
            else
            {
                switch (dir)
                {
                    case 1:
                        {
                            if (current_session.SessionPage.QCount > (SessionInterface.pageSize * (current_session.SessionPage.QPage + 1)))
                            {
                                lock (SessionInterface.syncLock)
                                {
                                    current_session.SessionPage.QPage++;
                                }
                            }
                        } break;
                    case -1:
                        {
                            if (current_session.SessionPage.QPage > 0)
                            {
                                lock (SessionInterface.syncLock)
                                {
                                    current_session.SessionPage.QPage--;
                                }
                            }
                        } break;
                    default: break;
                }

                foreach (var Rep in current_session.SessionReportList.Skip(current_session.SessionPage.QPage * SessionInterface.pageSize).Take(SessionInterface.pageSize))
                {
                    ReportObject GetReport = new ReportObject();

                    GetReport.ReportId = Rep.ReportId;
                    GetReport.ReportType = Rep.ReportType;
                    GetReport.ReportAuthor = Rep.ReportAuthor;
                    GetReport.ReportDescription = Rep.ReportDescription;
                    GetReport.ReportLocation = Rep.ReportLocation;
                    GetReport.ReportTime = Rep.ReportTime;
                    GetReport.ReportLatitude = Rep.ReportLatitude;
                    GetReport.ReportLongitude = Rep.ReportLongitude;
                    GetReport.ReportAccuracy = Rep.ReportAccuracy;
                    GetReport.ReportDirection = Rep.ReportDirection;

                    ReportList.Add(GetReport);
                }
            }
            return ReportList;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="type"></param>
        /// <param name="session_id"></param>
        /// <param name="isNew"></param>
        /// <param name="dir"></param>
        /// <param name="paged"></param>
        /// <param name="sort_select"></param>
        /// <returns></returns>
        public List<ReportObject> GetMyReportsByType(String type, String session_id, bool isNew, int dir, bool paged, int[] sort_select)
        {
            var ReportList = new List<ReportObject>();

            Session current_session = GetSession(session_id);

            if (isNew)
            {
                using (reportsEntities ReportData = new reportsEntities())
                {   
                    IOrderedQueryable<Report> reports;

                    switch (sort_select[0])
                    {
                        case 0:  // 0
                            {
                                switch (sort_select[1])
                                {
                                    case 0: // 00
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 000
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 0000
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                            case 1: // 0001
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime DESC").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 1: // 001
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 0010
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                            case 2: // 0012
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime DESC").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 2: // 002
                                                    {
                                                        // 0021
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime DESC").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner && report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 1: // 01
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 010
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 0100
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime
                                                                              select report;
                                                                } break;
                                                            case 2: // 0102
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 2: // 012
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 0120
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime
                                                                              select report;
                                                                } break;
                                                            case 3: // 0123
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 3: // 013
                                                    {
                                                        // 0132
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner && report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 2: // 02
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 020
                                                    {
                                                        // 0201
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 1: // 021
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 0210
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime
                                                                              select report;
                                                                } break;
                                                            case 3: // 0213
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 3: // 023
                                                    {
                                                        // 0231
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner && report.rtype == type
                                                                  orderby report.rtime
                                                                  select report;
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner && report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 3:
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 1: // 031
                                                    {
                                                        // 0312
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 2: // 032
                                                    {
                                                        // 0321
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner && report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    default:
                                        {
                                            reports = from report in ReportData.Reports
                                                      where report.rown == current_session.Owner && report.rtype == type
                                                      orderby report.rtime descending
                                                      select report;
                                        } break;
                                }
                            } break;
                        case 1: // 1
                            {
                                switch (sort_select[1])
                                {
                                    case 0: // 10
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 100
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 1000
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                            case 2: // 1002
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime DESC").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 2: // 102
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 1020
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                            case 3: // 1023
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime DESC").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 3: // 103
                                                    {
                                                        // 1032
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime DESC").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner && report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 2: // 12
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 120
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 1200
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime
                                                                              select report;
                                                                } break;
                                                            case 3: // 1203
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 3: // 123
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 1230
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime
                                                                              select report;
                                                                } break;
                                                            case 4: // 1234
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 4: // 124
                                                    {
                                                        // 1243
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner && report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 3: // 13
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 130
                                                    {
                                                        // 1302
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 2: // 132
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 1320
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime
                                                                              select report;
                                                                } break;
                                                            case 4: // 1324
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 4: // 134
                                                    {
                                                        // 1342
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner && report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 4: // 14
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 2: // 142
                                                    {
                                                        // 1423
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 3: // 143
                                                    {
                                                        // 1432
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner && report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    default:
                                        {
                                            reports = from report in ReportData.Reports
                                                      where report.rown == current_session.Owner && report.rtype == type
                                                      orderby report.rtime descending
                                                      select report;
                                        } break;
                                }
                            } break;
                        case 2: // 2
                            {
                                switch (sort_select[1])
                                {
                                    case 0: // 20
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 200
                                                    {
                                                        // 2001
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime DESC").AsQueryable();
                                                    } break;
                                                case 1: // 201
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 2010
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                            case 3: // 2013 // NO SORT ON STATUS YET
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime DESC").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 3: // 203
                                                    {
                                                        // 2031
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime DESC").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner && report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 1: // 21
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 210
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 2100
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime, report.rtype
                                                                              select report;
                                                                } break;
                                                            case 3: // 2103
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 3: // 213
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 2130
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime
                                                                              select report;
                                                                } break;
                                                            case 4: // 2134
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 4: // 214
                                                    {
                                                        // 2143
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner && report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 3: // 23
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 230
                                                    {
                                                        // 2301
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 1: // 231
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 2310
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime
                                                                              select report;
                                                                } break;
                                                            case 4: // 2314
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rown, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 4: // 234
                                                    {
                                                        // 2341
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner && report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 4: // 24
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 1: // 241
                                                    {
                                                        // 2413 
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 3: // 243
                                                    {
                                                        // 2431
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner && report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    default:
                                        {
                                            reports = from report in ReportData.Reports
                                                      where report.rown == current_session.Owner && report.rtype == type
                                                      orderby report.rtime descending
                                                      select report;
                                        } break;
                                }
                            } break;
                        case 3: // 3
                            {
                                switch (sort_select[1])
                                {
                                    case 0: // 30
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 1: // 301
                                                    {
                                                        // 3012
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime DESC").AsQueryable();
                                                    } break;
                                                case 2: // 302
                                                    {
                                                        // 3021
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime DESC").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner && report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 1: // 31
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 310
                                                    {
                                                        // 3102
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                    } break;
                                                case 2: // 312
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 3120
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime
                                                                              select report;
                                                                } break;
                                                            case 4: // 3124
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 4: // 314
                                                    {
                                                        // 3142
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner && report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 2: // 32
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 0: // 320
                                                    {
                                                        // 3201
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 1: // 321
                                                    {
                                                        switch (sort_select[3])
                                                        {
                                                            case 0: // 3210
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime
                                                                              select report;
                                                                } break;
                                                            case 4: // 3214
                                                                {
                                                                    reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                                } break;
                                                            default:
                                                                {
                                                                    reports = from report in ReportData.Reports
                                                                              where report.rown == current_session.Owner && report.rtype == type
                                                                              orderby report.rtime descending
                                                                              select report;
                                                                } break;
                                                        }
                                                    } break;
                                                case 4: // 324
                                                    {
                                                        // 3241
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner && report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 4: // 34
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 1: // 341
                                                    {
                                                        // 3412
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 2: // 342
                                                    {
                                                        // 3421
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner && report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    default:
                                        {
                                            reports = from report in ReportData.Reports
                                                      where report.rown == current_session.Owner && report.rtype == type
                                                      orderby report.rtime descending
                                                      select report;
                                        } break;
                                }
                            } break;
                        case 4: // 4
                            {

                                switch (sort_select[1])
                                {
                                    case 1: // 41
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 2: // 412
                                                    {
                                                        // 4123
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                    } break;
                                                case 3: // 413
                                                    {
                                                        // 4132
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner && report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 2: // 42
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 1: // 421
                                                    {
                                                        // 4213
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY r.rtime, m.rstat").AsQueryable();
                                                    } break;
                                                case 3: // 423
                                                    {
                                                        // 4231
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner && report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    case 3: // 43
                                        {
                                            switch (sort_select[2])
                                            {
                                                case 1: // 431
                                                    {
                                                        // 4312
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                case 2: // 432
                                                    {
                                                        // 4321
                                                        reports = (IOrderedQueryable<Report>)ReportData.Reports.SqlQuery(
                                                                                                         @"SELECT r.*
                                                                                                           FROM dbo.Reports AS r
                                                                                                           JOIN dbo.Managements AS m ON m.fid = r.rid
                                                                                                           WHERE r.rown = '" + current_session.Owner + "' AND r.rtype = '" + type +
                                                                                                           "' ORDER BY m.rstat, r.rtime").AsQueryable();
                                                    } break;
                                                default:
                                                    {
                                                        reports = from report in ReportData.Reports
                                                                  where report.rown == current_session.Owner && report.rtype == type
                                                                  orderby report.rtime descending
                                                                  select report;
                                                    } break;
                                            }
                                        } break;
                                    default:
                                        {
                                            reports = from report in ReportData.Reports
                                                      where report.rown == current_session.Owner && report.rtype == type
                                                      orderby report.rtime descending
                                                      select report;
                                        } break;
                                }
                            } break;
                        default:
                            {
                                reports = from report in ReportData.Reports
                                          where report.rown == current_session.Owner && report.rtype == type
                                          orderby report.rtime descending
                                          select report;
                            } break;
                    }

                    lock (SessionInterface.syncLock)
                    {
                        if (!paged)
                        {
                            current_session.SessionPage.QPage = 0;
                        }
                        current_session.SessionPage.QCount = reports.Count();
                    }

                    var FullReportList = new List<ReportObject>();

                    foreach (var Rep in reports.ToList())
                    {
                        ReportObject GetReport = new ReportObject();

                        GetReport.ReportId = Rep.rid;
                        GetReport.ReportType = Rep.rtype;
                        GetReport.ReportAuthor = Rep.rown;
                        GetReport.ReportDescription = Rep.rdesc;
                        GetReport.ReportLocation = Rep.rloc;
                        GetReport.ReportTime = Rep.rtime;
                        GetReport.ReportLatitude = Rep.rlat;
                        GetReport.ReportLongitude = Rep.rlon;
                        GetReport.ReportAccuracy = Rep.racc;
                        GetReport.ReportDirection = Rep.rdir;

                        FullReportList.Add(GetReport);
                    }

                    lock (SessionInterface.syncLock)
                    {
                        current_session.SessionPage.QTotal = FullReportList.Count;
                        current_session.SessionReportList = FullReportList;
                    }

                    if (paged)
                    {
                        foreach (var Rep in current_session.SessionReportList.Skip(current_session.SessionPage.QPage * SessionInterface.pageSize).Take(SessionInterface.pageSize))
                        {
                            ReportObject GetReport = new ReportObject();

                            GetReport.ReportId = Rep.ReportId;
                            GetReport.ReportType = Rep.ReportType;
                            GetReport.ReportAuthor = Rep.ReportAuthor;
                            GetReport.ReportDescription = Rep.ReportDescription;
                            GetReport.ReportLocation = Rep.ReportLocation;
                            GetReport.ReportTime = Rep.ReportTime;
                            GetReport.ReportLatitude = Rep.ReportLatitude;
                            GetReport.ReportLongitude = Rep.ReportLongitude;
                            GetReport.ReportAccuracy = Rep.ReportAccuracy;
                            GetReport.ReportDirection = Rep.ReportDirection;

                            ReportList.Add(GetReport);
                        }
                    }
                    else
                    {
                        foreach (var Rep in FullReportList.Take(SessionInterface.pageSize))
                        {
                            ReportObject GetReport = new ReportObject();

                            GetReport.ReportId = Rep.ReportId;
                            GetReport.ReportType = Rep.ReportType;
                            GetReport.ReportAuthor = Rep.ReportAuthor;
                            GetReport.ReportDescription = Rep.ReportDescription;
                            GetReport.ReportLocation = Rep.ReportLocation;
                            GetReport.ReportTime = Rep.ReportTime;
                            GetReport.ReportLatitude = Rep.ReportLatitude;
                            GetReport.ReportLongitude = Rep.ReportLongitude;
                            GetReport.ReportAccuracy = Rep.ReportAccuracy;
                            GetReport.ReportDirection = Rep.ReportDirection;

                            ReportList.Add(GetReport);
                        }
                    }
                }
            }
            else
            {
                switch (dir)
                {
                    case 1:
                        {
                            if (current_session.SessionPage.QCount > (SessionInterface.pageSize * (current_session.SessionPage.QPage + 1)))
                            {
                                lock (SessionInterface.syncLock)
                                {
                                    current_session.SessionPage.QPage++;
                                }
                            }
                        } break;
                    case -1:
                        {
                            if (current_session.SessionPage.QPage > 0)
                            {
                                lock (SessionInterface.syncLock)
                                {
                                    current_session.SessionPage.QPage--;
                                }
                            }
                        } break;
                    default: break;
                }

                foreach (var Rep in current_session.SessionReportList.Skip(current_session.SessionPage.QPage * SessionInterface.pageSize).Take(SessionInterface.pageSize))
                {
                    ReportObject GetReport = new ReportObject();

                    GetReport.ReportId = Rep.ReportId;
                    GetReport.ReportType = Rep.ReportType;
                    GetReport.ReportAuthor = Rep.ReportAuthor;
                    GetReport.ReportDescription = Rep.ReportDescription;
                    GetReport.ReportLocation = Rep.ReportLocation;
                    GetReport.ReportTime = Rep.ReportTime;
                    GetReport.ReportLatitude = Rep.ReportLatitude;
                    GetReport.ReportLongitude = Rep.ReportLongitude;
                    GetReport.ReportAccuracy = Rep.ReportAccuracy;
                    GetReport.ReportDirection = Rep.ReportDirection;

                    ReportList.Add(GetReport);
                }

            }
            return ReportList;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="session_id"></param>
        /// <returns></returns>
        public int[] GetPagingState(String session_id)
        {
            Session current_session = GetSession(session_id);
            return new int[] { current_session.SessionPage.QPage, current_session.SessionPage.QTotal };
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="composite"></param>
        /// <returns></returns>
        public PhotoObject GetPhoto(int id)
        {
            using (reportsEntities ReportData = new reportsEntities())
            {
                PhotoObject GetPhoto = new PhotoObject();
                IQueryable<byte[]> p = from photo in ReportData.Photos
                                       where photo.rid == id
                                       select photo.rimage;
                GetPhoto.ReportId = id;
                GetPhoto.ReportPhoto = p.FirstOrDefault();
                return GetPhoto;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public bool CloseReport(int id)
        {
            using (reportsEntities ReportData = new reportsEntities())
            {
                var query = from report in ReportData.Managements
                            where report.fid == id
                            select report;

                query.FirstOrDefault().rstat = "Closed";

                // Submit the changes to the database.
                try
                {
                    ReportData.SaveChanges();
                    return true;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    // Provide for exceptions.
                }
            }
            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public bool OpenReport(int id)
        {
            using (reportsEntities ReportData = new reportsEntities())
            {
                var query = from report in ReportData.Managements
                            where report.fid == id
                            select report;

                query.FirstOrDefault().rstat = "Open";

                // Submit the changes to the database.
                try
                {
                    ReportData.SaveChanges();
                    return true;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    // Provide for exceptions.
                }
            }
            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public bool ElevateReport(int id)
        {
            using (reportsEntities ReportData = new reportsEntities())
            {
                var query = from report in ReportData.Managements
                            where report.fid == id
                            select report;

                query.FirstOrDefault().rprio = "Urgent";

                // Submit the changes to the database.
                try
                {
                    ReportData.SaveChanges();
                    return true;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    // Provide for exceptions.
                }
            }
            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="id"></param>
        /// <param name="admin"></param>
        /// <returns></returns>
        public bool ClaimReport(int id, String admin)
        {
            using (reportsEntities ReportData = new reportsEntities())
            {
                var query = from report in ReportData.Reports
                            where report.rid == id
                            select report;

                query.FirstOrDefault().rown = admin;

                // Submit the changes to the database.
                try
                {
                    ReportData.SaveChanges();
                    return true;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    // Provide for exceptions.
                }
            }
            return false;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="composite"></param>
        /// <returns></returns>
        public string GetStatus(int id)
        {
            using (reportsEntities ReportData = new reportsEntities())
            {
                IQueryable<String> m = from management in ReportData.Managements
                                       where management.fid == id
                                       select management.rstat;

                return m.FirstOrDefault();
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="composite"></param>
        /// <returns></returns>
        public ReportManagement GetReportManagement(int id)
        {
            using (reportsEntities ReportData = new reportsEntities())
            {
                var m = from management in ReportData.Managements
                                           where management.fid == id
                                           select management;

                ReportManagement managementObject = new ReportManagement();
                managementObject.ReportId = m.FirstOrDefault().rid;
                managementObject.ReportStatus = m.FirstOrDefault().rstat;
                managementObject.ReportPriority = m.FirstOrDefault().rprio;
                managementObject.ReportFrequency = m.FirstOrDefault().rfreq;
                managementObject.ReportForeignKey = m.FirstOrDefault().fid;

                return managementObject;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public bool RemoveReport(String name, int id)
        {
            using (reportsEntities ReportData = new reportsEntities())
            {
                var photos = from photo in ReportData.Photos
                             where photo.fid == id
                             select photo;

                var managements = from management in ReportData.Managements
                                  where management.fid == id
                                  select management;

                var reports = from report in ReportData.Reports
                              where report.rid == id
                              select report;

                if (ReportData.Reports.Count() >= 1)
                {
                    if (name == reports.First().rown)
                    {
                        ReportData.Reports.Remove(reports.First());
                        ReportData.Photos.Remove(photos.First());
                        ReportData.Managements.Remove(managements.First());
                        ReportData.SaveChanges();
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
        }
    }

    // MAY CONSUME A LOT OF MEMORY IF MANY SESSIONS
    // IN ORDER TO SCALE WOULD REQUIRE SHARING LIST BETWEEN SESSIONS

    public class Session
    {
        private List<ReportObject> sessionReportList;
        private List<ReportManagement> sessionReportManagementList;

        private Page sessionPage;

        private string sessionID = "";
        private string owner = "";

        private bool[] rep_pv_pair = { false, false };

        private ReportObject lastReportObject;
        private PhotoObject lastPhotoObject;
        private ReportManagement lastReportManagement;

        private System.Threading.Timer timer;

        public Session()
        {
            sessionReportList = new List<ReportObject>();
            sessionReportManagementList = new List<ReportManagement>();
            sessionPage = new Page();
            lastReportObject = new ReportObject();
            lastPhotoObject = new PhotoObject();
            lastReportManagement = new ReportManagement();
        }

        public System.Threading.Timer Timer
        {
            get { return timer; }
            set { timer = value; }
        }

        public ReportObject LastReportObject
        {
            get { return lastReportObject; }
            set { lastReportObject = value; }
        }

        public PhotoObject LastPhotoObject
        {
            get { return lastPhotoObject; }
            set { lastPhotoObject = value; }
        }

        public ReportManagement LastReportManagement
        {
            get { return lastReportManagement; }
            set { lastReportManagement = value; }
        }

        public bool ReportHalf
        {
            get { return rep_pv_pair[0]; }
            set { rep_pv_pair[0] = value; }
        }

        public bool PVHalf
        {
            get { return rep_pv_pair[1]; }
            set { rep_pv_pair[1] = value; }
        }


        public List<ReportObject> SessionReportList
        {
            get { return sessionReportList; }
            set { sessionReportList = value; }
        }

        public List<ReportManagement> SessionReportManagementList
        {
            get { return sessionReportManagementList; }
            set { sessionReportManagementList = value; }
        }

        public Page SessionPage
        {
            get { return sessionPage; }
            set { sessionPage = value; }
        }

        public string SessionID
        {
            get { return sessionID; }
            set { sessionID = value; }
        }

        public string Owner
        {
            get { return owner; }
            set { owner = value; }
        }
    }

    public class Page
    {
        private int page;
        private int count;
        private int total;

        public Page()
        {
            page = 0;
            count = 0;
            total = 0;
        }

        public int QPage
        {
            get { return page; }
            set { page = value; }
        }
        public int QCount
        {
            get { return count; }
            set { count = value; }
        }
        public int QTotal
        {
            get { return total; }
            set { total = value; }
        }
    }

    public enum QueryType
    {
        All,
        My,
        AllByType,
        MyByType
    };

    public class Mail : IMail
    {
        private string SMTP_SERVER = "smtp.gmail.com";
        private int SMTP_PORT = 587;
        private string TEMP_FOLDER = @"C:\Temp\";

        public int SendEmail(string gmailUserAddress, string gmailUserPassword, string[] emailTo, string[] ccTo, string subject, string body, bool isBodyHtml, FileAttachment[] attachments, byte[] image_bytes)
        {
            int result = -100;
            if (gmailUserAddress == null || gmailUserAddress.Trim().Length == 0)
            {
                return 10;
            }
            if (gmailUserPassword == null || gmailUserPassword.Trim().Length == 0)
            {
                return 20;
            }
            if (emailTo == null || emailTo.Length == 0)
            {
                return 30;
            }

            string tempFilePath = "";
            List<string> tempFiles = new List<string>();

            SmtpClient smtpClient = new SmtpClient(SMTP_SERVER, SMTP_PORT);
            smtpClient.EnableSsl = true;
            smtpClient.DeliveryMethod = SmtpDeliveryMethod.Network;
            smtpClient.UseDefaultCredentials = false;
            smtpClient.Credentials = new NetworkCredential(gmailUserAddress, gmailUserPassword);

            Bitmap mbitmap = ByteArrayToBitmap(image_bytes);
            System.IO.MemoryStream mstream = new System.IO.MemoryStream();
            mbitmap.Save(mstream, System.Drawing.Imaging.ImageFormat.Jpeg);
            mstream.Position = 0;
            //System.Net.Mail.LinkedResource mres = new System.Net.Mail.LinkedResource(mstream);

            ContentType contentType = new ContentType();
            contentType.MediaType = MediaTypeNames.Image.Jpeg;
            contentType.Name = "photo.jpeg";

            using (MailMessage message = new MailMessage())
            //Message object must be disposed before deleting temp attachment files
            {
                message.From = new MailAddress(gmailUserAddress);
                message.Subject = subject == null ? "" : subject;
                message.Body = body == null ? "" : body;
                message.IsBodyHtml = isBodyHtml;

                foreach (string email in emailTo)
                {
                    //TODO: Check email is valid
                    message.To.Add(email);
                }

                if (ccTo != null && ccTo.Length > 0)
                {
                    foreach (string emailCc in ccTo)
                    {
                        //TODO: Check CC email is valid
                        message.CC.Add(emailCc);
                    }
                }

                message.Attachments.Add(new Attachment(mstream, contentType));

                if (attachments != null && attachments.Length > 0)
                {
                    foreach (FileAttachment fileAttachment in attachments)
                    {
                        if (fileAttachment.Info == null || fileAttachment.FileContentBase64 == null)
                        {
                            continue;
                        }

                        tempFilePath = CreateTempFile(TEMP_FOLDER, fileAttachment.FileContentBase64);

                        if (tempFilePath != null && tempFilePath.Length > 0)
                        {
                            Attachment attachment = new Attachment(tempFilePath, MediaTypeNames.Application.Octet);
                            ContentDisposition disposition = attachment.ContentDisposition;
                            disposition.FileName = fileAttachment.Info.Name;
                            disposition.CreationDate = fileAttachment.Info.CreationTime;
                            disposition.ModificationDate = fileAttachment.Info.LastWriteTime;
                            disposition.ReadDate = fileAttachment.Info.LastAccessTime;
                            disposition.DispositionType = DispositionTypeNames.Attachment;
                            message.Attachments.Add(attachment);
                            tempFiles.Add(tempFilePath);
                        }
                        else
                        {
                            return 50;
                        }
                    }
                }

                try
                {
                    smtpClient.Send(message);
                    result = 0;
                }
                catch
                {
                    result = 60;
                }
            }

            DeleteTempFiles(tempFiles.ToArray());
            return result;
        }

        private Bitmap ByteArrayToBitmap(byte[] m_byteArrayIn)
        {
            MemoryStream oMemoryStream = new MemoryStream(m_byteArrayIn);
            Image oImage = Image.FromStream(oMemoryStream);
            Bitmap bitmap = new Bitmap(oImage);
            return bitmap;
        }

        private static string CreateTempFile(string destDir, string fileContentBase64)
        {
            string tempFilePath = destDir + (destDir.EndsWith("\\") ? "" : "\\") + Guid.NewGuid().ToString();

            try
            {
                using (FileStream fs = new FileStream(tempFilePath, FileMode.Create))
                {
                    byte[] bytes = System.Convert.FromBase64String(fileContentBase64); ;
                    fs.Write(bytes, 0, bytes.Length);
                }
            }
            catch
            {
                return null;
            }

            return tempFilePath;
        }

        private static void DeleteTempFiles(string[] tempFiles)
        {
            if (tempFiles != null && tempFiles.Length > 0)
            {
                foreach (string filePath in tempFiles)
                {
                    if (File.Exists(filePath))
                    {
                        try
                        {
                            File.Delete(filePath);
                        }
                        catch { } //Do nothing
                    }
                }
            }
        }
    }
}