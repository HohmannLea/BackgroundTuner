using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackgroundTuner.Services
{
    internal class BRMLReader
    {
        private readonly ArchiveHandler _archiveHandler;
        public BRMLReader(ArchiveHandler archiveHandler)
        {
            _archiveHandler = archiveHandler;
        }

    }
}
