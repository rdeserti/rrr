using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using rrr.Core;

namespace rrr.Imaging;

public interface IImageReader
{
    /// <summary>
    /// Decodes an image from a stream into a <see cref="FrameBuffer"/>.
    /// This is the core method each reader implements; it must not assume
    /// the stream is seekable beyond what the format requires.
    /// </summary>
    FrameBuffer Load(Stream stream);

    /// <summary>
    /// Convenience wrapper that opens <paramref name="fileName"/> and
    /// decodes it via <see cref="Load(Stream)"/>.
    /// </summary>
    FrameBuffer Load(string fileName)
    {
        using FileStream stream =
            File.OpenRead(fileName);

        return Load(stream);
    }
}
