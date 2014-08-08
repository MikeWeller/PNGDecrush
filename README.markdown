## PNGDecrush

PNGDecrush is a C# library for reversing the optimization process that is applied to PNG files in an iOS project.

现在支持.Net 2.0

### Background

When building an iOS app using Xcode, PNG files are run through the `pngcrush` tool with some proprietry Apple modifications (accessed through the `-iphone` option).

`xcrun -sdk iphoneos pngcrush -iphone ...`

Specifically, the following steps are performed:

* The proprietry and non-standard `CgBI` chunk is added to the start of the PNG.
* Zlib headers and checksums are removed from all IDAT chunks (leaving just raw deflate data).
* Pixel data is converted to BGR/BGRA byte order.
* Pixel color values are pre-multiplied with the alpha.

### Existing implementations are broken

There are some existing 'decrush' implementations out there if you google around, all of which are broken for a number of different reasons, including:

* They perform a very naive byte swap on the raw PNG image bytes without reversing the precompression line filters beforehand. This will cause sometimes subtle, sometimes not-so-subtle artifacts in the resulting image.
* They do not handle multiple IDAT chunks correctly.
* Their code quality is lacking and they do not have tests.

### This implementation

This implemention performs the following steps:

* The `CgBI` chunk is removed
* Chunk data is inflated and deflated again with zlib headers intact
* The resulting PNG is read into .NET's standard image manipulation classes where:
    - The byte swap is reversed
    - The premultiplied alpha is reversed

We have unit tests, and everything is stream based.

A simple example of using the library:

    using (FileStream input = File.OpenRead('/path/to/input.png'))
    using (FileStream output = File.Create('/path/to/output.png'))
    {
    	try
    	{
    	    PNGDecrusher.Decrush(input, output);
    	}
    	catch (InvalidDataException)
        {
            // decrushing failed, either an invalid PNG or it wasn't crushed
        }
    }
