//------------------------------------------------------------------------------
// <auto-generated>
//    This code was generated from a template.
//
//    Manual changes to this file may cause unexpected behavior in your application.
//    Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace SIUC311Services
{
    using System;
    using System.Collections.Generic;
    
    public partial class Management
    {
        public int rid { get; set; }
        public string rstat { get; set; }
        public string rprio { get; set; }
        public int rfreq { get; set; }
        public int fid { get; set; }
    
        public virtual Report Report { get; set; }
    }
}
