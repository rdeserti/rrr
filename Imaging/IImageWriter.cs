using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using rrr.Core;

namespace rrr.Imaging;

public interface IImageWriter
{
    void Save(
        FrameBuffer frameBuffer,
        string fileName,
        ImageWriteSettings? settings = null);
}
