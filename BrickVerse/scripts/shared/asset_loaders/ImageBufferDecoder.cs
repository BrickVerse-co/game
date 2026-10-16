// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Text;
using Godot;

namespace BrickVerse.Shared.AssetLoaders;

internal static class ImageBufferDecoder
{
	public static Image Decode(byte[] buffer, string? source = null)
	{
		ArgumentNullException.ThrowIfNull(buffer);
		if (buffer.Length == 0)
			throw new InvalidDataException($"Image data is empty{FormatSource(source)}.");

		Image image = new();
		Error error = DetectFormat(buffer, source) switch
		{
			ImageFormat.Png => image.LoadPngFromBuffer(buffer),
			ImageFormat.Jpeg => image.LoadJpgFromBuffer(buffer),
			ImageFormat.Webp => image.LoadWebpFromBuffer(buffer),
			ImageFormat.Svg => image.LoadSvgFromBuffer(buffer),
			_ => throw new InvalidDataException($"Unsupported image data{FormatSource(source)}."),
		};

		if (error != Error.Ok || image.GetWidth() <= 0 || image.GetHeight() <= 0)
			throw new InvalidDataException($"Could not decode image{FormatSource(source)} ({error}).");

		return image;
	}

	private static ImageFormat DetectFormat(byte[] buffer, string? source)
	{
		ReadOnlySpan<byte> bytes = buffer;
		if (
			bytes.Length >= 8
			&& bytes[0] == 0x89
			&& bytes.Slice(1, 7).SequenceEqual("PNG\r\n\x1a\n"u8)
		)
			return ImageFormat.Png;
		if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff)
			return ImageFormat.Jpeg;
		if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
			return ImageFormat.Webp;
		if (LooksLikeSvg(bytes))
			return ImageFormat.Svg;

		string extension = GetExtension(source);
		return extension switch
		{
			".png" => ImageFormat.Png,
			".jpg" or ".jpeg" => ImageFormat.Jpeg,
			".webp" => ImageFormat.Webp,
			".svg" => ImageFormat.Svg,
			_ => ImageFormat.Unknown,
		};
	}

	private static bool LooksLikeSvg(ReadOnlySpan<byte> bytes)
	{
		int length = Math.Min(bytes.Length, 512);
		string header = Encoding.UTF8.GetString(bytes[..length]);
		return header.Contains("<svg", StringComparison.OrdinalIgnoreCase);
	}

	private static string GetExtension(string? source)
	{
		if (string.IsNullOrWhiteSpace(source)) return string.Empty;
		if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri)) source = uri.AbsolutePath;
		return Path.GetExtension(source).ToLowerInvariant();
	}

	private static string FormatSource(string? source) =>
		string.IsNullOrWhiteSpace(source) ? string.Empty : $" from {source}";

	private enum ImageFormat
	{
		Unknown,
		Png,
		Jpeg,
		Webp,
		Svg,
	}
}
