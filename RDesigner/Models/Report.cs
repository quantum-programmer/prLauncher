using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RDesigner.Models
{
    public class ARMReport  
    {
        public int ARMReportID { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? ModifyDate { get; set; }
        public int CodeAssociatePO { get; set; }
        public int ParentID { get; set; }        
        public int UniqueID { get; set; }        
        public byte[]? reportData { get; set; }
        public Boolean isFolder {  get; set; }
        public Boolean isDelete {  get; set; }
    }
}
