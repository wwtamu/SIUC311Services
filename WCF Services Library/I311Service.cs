using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;
using System.Drawing;
using System.Timers;

namespace SIUC311Services
{
    [ServiceContract]
    public interface I311Service
    {
        [OperationContract]
        string BeginSession(String name);

        [OperationContract]
        bool CheckPermissions(String session_id);

        [OperationContract]
        bool IsSessionAlive(String session_id);
        
        [OperationContract]
        bool EndSession(String session_id);

        [OperationContract]
        bool InsertReport(String session_id, ReportObject RepInsert);

        [OperationContract]
        bool InsertPhoto(String session_id, PhotoObject PhoInsert);

        [OperationContract]
        bool RemoveReport(String name, int id);

        [OperationContract]
        List<ReportObject> GetAllReports(String session_id, bool isNew, int dir, bool paged, int[] sort_select);

        [OperationContract]
        List<ReportObject> GetMyReports(String session_id, bool isNew, int dir, bool paged, int[] sort_select);
        
        [OperationContract]
        List<ReportObject> GetAllReportsByType(String type, String session_id, bool isNew, int dir, bool paged, int[] sort_select);

        [OperationContract]
        List<ReportObject> GetMyReportsByType(String type, String session_id, bool isNew, int dir, bool paged, int[] sort_select);

        [OperationContract]
        int[] GetPagingState(String session_id);

        [OperationContract]
        PhotoObject GetPhoto(int id);

        [OperationContract]
        string GetStatus(int id);

        [OperationContract]
        ReportManagement GetReportManagement(int id);

        [OperationContract]
        bool CloseReport(int id);

        [OperationContract]
        bool OpenReport(int id);

        [OperationContract]
        bool ElevateReport(int id);

        [OperationContract]
        bool ClaimReport(int id, String admin);
    }
    
    [DataContract] public class PhotoObject
    {                                       // Microsoft SQL dbo.Photos
        private int report_id;              // rid    int primary key, foreign key references PhotoReports.rid
        private byte[] report_photo;        // rimage varbinary(MAX)
        private int report_foreign_key;     // fid   int FOREIGN KEY REFERENCES Reports(rid)

        [DataMember]
        public int ReportId
        {
            get { return report_id; }
            set { report_id = value; }
        }
        [DataMember]
        public byte[] ReportPhoto
        {
            get { return report_photo; }
            set { report_photo = value; }
        }
        [DataMember]
        public int ReportForeignKey
        {
            get { return report_foreign_key; }
            set { report_foreign_key = value; }
        }
    }

    [DataContract] public class ReportManagement
    {                                       // Microsoft SQL dbo.Managements
        private int report_id;              // rid    int primary key identity
        private string report_status;       // rstat varchar(20)
        private string report_priority;     // rprio varchar(20)
        private int report_frequency;       // rfreq int
        private int report_foreign_key;     // fid   int FOREIGN KEY REFERENCES Reports(rid)
        
        [DataMember]
        public int ReportId
        {
            get { return report_id; }
            set { report_id = value; }
        }
        [DataMember]
        public string ReportStatus
        {
            get { return report_status; }
            set { report_status = value; }
        }
        [DataMember]
        public string ReportPriority
        {
            get { return report_priority; }
            set { report_priority = value; }
        }
        [DataMember]
        public int ReportFrequency
        {
            get { return report_frequency; }
            set { report_frequency = value; }
        }
        [DataMember]
        public int ReportForeignKey
        {
            get { return report_foreign_key; }
            set { report_foreign_key = value; }
        }
    }

    [DataContract] public class ReportObject
    {                                       // Microsoft SQL dbo.Reports
        private int report_id;              // rid   int primary key identity
        private string report_type;         // rtype varchar(50)
        private string report_author;       // rown  varchar(50)
        private string report_description;  // rdesc varchar(500) NULL
        private string report_location;     // rloc varchar(250) NULL
        private DateTime report_time;       // rtime datetime
        private string report_latitude;     // rlat  varchar(20)        
        private string report_longitude;    // rlon  varchar(20)
        private string report_accuracy;     // racc  varchar(20)
        private string report_direction;    // rdir  varchar(20)

        [DataMember] public int ReportId
        {
            get { return report_id; }
            set { report_id = value; }
        }
        [DataMember]
        public string ReportAuthor
        {
            get { return report_author; }
            set { report_author = value; }
        }
        [DataMember] public string ReportType
        {
            get { return report_type; }
            set { report_type = value; }
        }
        [DataMember] public string ReportDescription
        {
            get { return report_description; }
            set { report_description = value; }
        }
        [DataMember]
        public string ReportLocation
        {
            get { return report_location; }
            set { report_location = value; }
        }
        [DataMember] public DateTime ReportTime
        {
            get { return report_time; }
            set { report_time = value; }
        }
        [DataMember] public string ReportLatitude
        {
            get { return report_latitude; }
            set { report_latitude = value; }
        }
        [DataMember] public string ReportLongitude
        {
            get { return report_longitude; }
            set { report_longitude = value; }
        }
        [DataMember]
        public string ReportAccuracy
        {
            get { return report_accuracy; }
            set { report_accuracy = value; }
        }
        [DataMember] public string ReportDirection
        {
            get { return report_direction; }
            set { report_direction = value; }
        }
    }

    [ServiceContract]
    public interface IMail
    {

        [OperationContract]
        int SendEmail(string gmailUserAddress, string gmailUserPassword, string[] emailTo, string[] ccTo, string subject, string body, bool isBodyHtml, FileAttachment[] attachments, byte[] image_bytes);

    }

    [DataContract] public class FileAttachment
    {
        [DataMember]
        public string FileContentBase64 { get; set; }

        [DataMember]
        public Image FileImage { get; set; }

        [DataMember]
        public FileInfo Info { get; set; }
    }
}
