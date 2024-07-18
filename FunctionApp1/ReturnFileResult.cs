using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EncryptFiles
{
    public class ReturnFileResult
    {
        public FileContentResult File { get; set; }
        public string FileName { get; set; }
    }
}
