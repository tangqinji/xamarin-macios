#if !__WATCHOS__

using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

using NUnit.Framework;

#if XAMCORE_2_0
using AudioToolbox;
using AudioUnit;
using CoreMedia;
using CoreFoundation;
using CoreGraphics;
using CoreText;
using CoreServices;
using CoreVideo;
using Foundation;
using ImageIO;
using MediaToolbox;
using SystemConfiguration;
using ObjCRuntime;
using Security;
using VideoToolbox;
using UIKit;
using Network;
#else
using MonoTouch.AudioToolbox;
using MonoTouch.CoreMedia;
using MonoTouch.CoreFoundation;
using MonoTouch.CoreGraphics;
using MonoTouch.CoreServices;
using MonoTouch.CoreText;
using MonoTouch.CoreVideo;
using MonoTouch.Foundation;
using MonoTouch.ImageIO;
using MonoTouch.SystemConfiguration;
using MonoTouch.ObjCRuntime;
using MonoTouch.Security;
using MonoTouch.VideoToolbox;
using MonoTouch.UIKit;
using MonoTouch.Network;
#endif

namespace Introspection {

	[TestFixture]
	public class ApiCMAttachmentTest : ApiBaseTest {
		static Type CMClockType = typeof (CMClock);
		static Type CMAttachmentInterfaceType = typeof (ICMAttachmentBearer);
		static Type NativeObjectInterfaceType = typeof (INativeObject);
		static Type DispatchSourceType = typeof (DispatchSource);
		// CN=mail.google.com, O=Google Inc, L=Mountain View, S=California, C=US
		static public byte[] mail_google_com = {
			0x30, 0x82, 0x03, 0x22, 0x30, 0x82, 0x02, 0x8b, 0xa0, 0x03, 0x02, 0x01,
			0x02, 0x02, 0x10, 0x2b, 0x9f, 0x7e, 0xe5, 0xca, 0x25, 0xa6, 0x25, 0x14,
			0x20, 0x47, 0x82, 0x75, 0x3a, 0x9b, 0xb9, 0x30, 0x0d, 0x06, 0x09, 0x2a,
			0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x05, 0x05, 0x00, 0x30, 0x4c,
			0x31, 0x0b, 0x30, 0x09, 0x06, 0x03, 0x55, 0x04, 0x06, 0x13, 0x02, 0x5a,
			0x41, 0x31, 0x25, 0x30, 0x23, 0x06, 0x03, 0x55, 0x04, 0x0a, 0x13, 0x1c,
			0x54, 0x68, 0x61, 0x77, 0x74, 0x65, 0x20, 0x43, 0x6f, 0x6e, 0x73, 0x75,
			0x6c, 0x74, 0x69, 0x6e, 0x67, 0x20, 0x28, 0x50, 0x74, 0x79, 0x29, 0x20,
			0x4c, 0x74, 0x64, 0x2e, 0x31, 0x16, 0x30, 0x14, 0x06, 0x03, 0x55, 0x04,
			0x03, 0x13, 0x0d, 0x54, 0x68, 0x61, 0x77, 0x74, 0x65, 0x20, 0x53, 0x47,
			0x43, 0x20, 0x43, 0x41, 0x30, 0x1e, 0x17, 0x0d, 0x31, 0x31, 0x31, 0x30,
			0x32, 0x36, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x5a, 0x17, 0x0d, 0x31,
			0x33, 0x30, 0x39, 0x33, 0x30, 0x32, 0x33, 0x35, 0x39, 0x35, 0x39, 0x5a,
			0x30, 0x69, 0x31, 0x0b, 0x30, 0x09, 0x06, 0x03, 0x55, 0x04, 0x06, 0x13,
			0x02, 0x55, 0x53, 0x31, 0x13, 0x30, 0x11, 0x06, 0x03, 0x55, 0x04, 0x08,
			0x13, 0x0a, 0x43, 0x61, 0x6c, 0x69, 0x66, 0x6f, 0x72, 0x6e, 0x69, 0x61,
			0x31, 0x16, 0x30, 0x14, 0x06, 0x03, 0x55, 0x04, 0x07, 0x14, 0x0d, 0x4d,
			0x6f, 0x75, 0x6e, 0x74, 0x61, 0x69, 0x6e, 0x20, 0x56, 0x69, 0x65, 0x77,
			0x31, 0x13, 0x30, 0x11, 0x06, 0x03, 0x55, 0x04, 0x0a, 0x14, 0x0a, 0x47,
			0x6f, 0x6f, 0x67, 0x6c, 0x65, 0x20, 0x49, 0x6e, 0x63, 0x31, 0x18, 0x30,
			0x16, 0x06, 0x03, 0x55, 0x04, 0x03, 0x14, 0x0f, 0x6d, 0x61, 0x69, 0x6c,
			0x2e, 0x67, 0x6f, 0x6f, 0x67, 0x6c, 0x65, 0x2e, 0x63, 0x6f, 0x6d, 0x30,
			0x81, 0x9f, 0x30, 0x0d, 0x06, 0x09, 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d,
			0x01, 0x01, 0x01, 0x05, 0x00, 0x03, 0x81, 0x8d, 0x00, 0x30, 0x81, 0x89,
			0x02, 0x81, 0x81, 0x00, 0xaf, 0x39, 0x15, 0x98, 0x68, 0xe4, 0x92, 0xfe,
			0x4f, 0x4f, 0xf1, 0xbb, 0xff, 0x0d, 0x2e, 0xb0, 0xfe, 0x25, 0xaa, 0xbd,
			0x68, 0x04, 0x67, 0x27, 0xea, 0x6c, 0x43, 0x4c, 0xa7, 0x6d, 0xcb, 0xc8,
			0x8f, 0x7e, 0x81, 0xee, 0x87, 0x26, 0x25, 0x10, 0x12, 0x54, 0x33, 0x9e,
			0xaa, 0x3d, 0x9b, 0x8f, 0x8e, 0x92, 0xb3, 0x4b, 0x01, 0xe3, 0xf9, 0x4a,
			0x29, 0xc3, 0x0f, 0xfd, 0xac, 0xb7, 0xd3, 0x4c, 0x97, 0x29, 0x3f, 0x69,
			0x55, 0xcf, 0x70, 0x83, 0x04, 0xaf, 0x2e, 0x04, 0x6e, 0x74, 0xd6, 0x0f,
			0x17, 0x09, 0xfe, 0x9e, 0x20, 0x24, 0x24, 0xe3, 0xc7, 0x68, 0x9c, 0xac,
			0x11, 0xbd, 0x92, 0xe4, 0xb2, 0x1b, 0x09, 0xf2, 0x02, 0x32, 0xbb, 0x55,
			0x1b, 0x2d, 0x16, 0x5f, 0x30, 0x12, 0x23, 0xe2, 0x4c, 0x4a, 0x8d, 0xc2,
			0xda, 0x3f, 0xe1, 0xb8, 0xbf, 0xf7, 0x3a, 0xb1, 0x86, 0xbe, 0xf0, 0xc5,
			0x02, 0x03, 0x01, 0x00, 0x01, 0xa3, 0x81, 0xe7, 0x30, 0x81, 0xe4, 0x30,
			0x0c, 0x06, 0x03, 0x55, 0x1d, 0x13, 0x01, 0x01, 0xff, 0x04, 0x02, 0x30,
			0x00, 0x30, 0x36, 0x06, 0x03, 0x55, 0x1d, 0x1f, 0x04, 0x2f, 0x30, 0x2d,
			0x30, 0x2b, 0xa0, 0x29, 0xa0, 0x27, 0x86, 0x25, 0x68, 0x74, 0x74, 0x70,
			0x3a, 0x2f, 0x2f, 0x63, 0x72, 0x6c, 0x2e, 0x74, 0x68, 0x61, 0x77, 0x74,
			0x65, 0x2e, 0x63, 0x6f, 0x6d, 0x2f, 0x54, 0x68, 0x61, 0x77, 0x74, 0x65,
			0x53, 0x47, 0x43, 0x43, 0x41, 0x2e, 0x63, 0x72, 0x6c, 0x30, 0x28, 0x06,
			0x03, 0x55, 0x1d, 0x25, 0x04, 0x21, 0x30, 0x1f, 0x06, 0x08, 0x2b, 0x06,
			0x01, 0x05, 0x05, 0x07, 0x03, 0x01, 0x06, 0x08, 0x2b, 0x06, 0x01, 0x05,
			0x05, 0x07, 0x03, 0x02, 0x06, 0x09, 0x60, 0x86, 0x48, 0x01, 0x86, 0xf8,
			0x42, 0x04, 0x01, 0x30, 0x72, 0x06, 0x08, 0x2b, 0x06, 0x01, 0x05, 0x05,
			0x07, 0x01, 0x01, 0x04, 0x66, 0x30, 0x64, 0x30, 0x22, 0x06, 0x08, 0x2b,
			0x06, 0x01, 0x05, 0x05, 0x07, 0x30, 0x01, 0x86, 0x16, 0x68, 0x74, 0x74,
			0x70, 0x3a, 0x2f, 0x2f, 0x6f, 0x63, 0x73, 0x70, 0x2e, 0x74, 0x68, 0x61,
			0x77, 0x74, 0x65, 0x2e, 0x63, 0x6f, 0x6d, 0x30, 0x3e, 0x06, 0x08, 0x2b,
			0x06, 0x01, 0x05, 0x05, 0x07, 0x30, 0x02, 0x86, 0x32, 0x68, 0x74, 0x74,
			0x70, 0x3a, 0x2f, 0x2f, 0x77, 0x77, 0x77, 0x2e, 0x74, 0x68, 0x61, 0x77,
			0x74, 0x65, 0x2e, 0x63, 0x6f, 0x6d, 0x2f, 0x72, 0x65, 0x70, 0x6f, 0x73,
			0x69, 0x74, 0x6f, 0x72, 0x79, 0x2f, 0x54, 0x68, 0x61, 0x77, 0x74, 0x65,
			0x5f, 0x53, 0x47, 0x43, 0x5f, 0x43, 0x41, 0x2e, 0x63, 0x72, 0x74, 0x30,
			0x0d, 0x06, 0x09, 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x05,
			0x05, 0x00, 0x03, 0x81, 0x81, 0x00, 0x35, 0x80, 0x11, 0xcd, 0x52, 0x3e,
			0x84, 0x29, 0xfb, 0xc1, 0x28, 0xe1, 0x20, 0xe5, 0x02, 0x8f, 0x5f, 0x71,
			0x65, 0x58, 0x1d, 0x62, 0x72, 0x57, 0x3c, 0xe6, 0x5e, 0x25, 0x61, 0xd3,
			0xcb, 0xad, 0x22, 0xf8, 0xd8, 0x81, 0xa4, 0xe7, 0xf4, 0xae, 0x7c, 0xd9,
			0xc1, 0x6d, 0xaa, 0x93, 0x0d, 0x62, 0x07, 0x9f, 0xf2, 0x67, 0x47, 0x99,
			0x34, 0x33, 0x4f, 0x3d, 0x02, 0x74, 0xf4, 0x81, 0xd6, 0x38, 0x08, 0x21,
			0xe8, 0xe2, 0xa1, 0xfa, 0x05, 0x41, 0x9c, 0x9c, 0xc9, 0xf9, 0xf3, 0xc8,
			0xa3, 0xee, 0x0d, 0xa5, 0xd7, 0x50, 0x54, 0x5e, 0x2f, 0x7d, 0x79, 0xb7,
			0x7e, 0x0a, 0x7c, 0xb6, 0xe2, 0x2c, 0xa8, 0xae, 0xfe, 0x94, 0xd7, 0xcd,
			0x16, 0x30, 0x71, 0x04, 0xaa, 0x9e, 0x79, 0xc3, 0xd2, 0xb6, 0x24, 0xa7,
			0x25, 0xab, 0xf0, 0x48, 0x8e, 0x2f, 0xc3, 0xa7, 0xbb, 0x50, 0xdd, 0x0f,
			0xcf, 0xb0,  };
		
		// copy-pasted from mono/mcs/class/corlib/Test/System.Security.Cryptography.X509Certificates/X509Cert20Test.cs
		static public byte[] farscape_pfx = { 0x30, 0x82, 0x06, 0xA3, 0x02, 0x01, 0x03, 0x30, 0x82, 0x06, 0x63, 0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x07, 0x01, 0xA0, 0x82, 0x06, 0x54, 0x04, 0x82, 0x06, 0x50, 0x30, 0x82, 0x06, 0x4C, 0x30, 0x82, 0x03, 0x8D, 0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x07, 0x01, 0xA0, 0x82, 0x03, 0x7E, 0x04, 0x82, 0x03, 0x7A, 0x30, 0x82, 0x03, 0x76, 0x30, 0x82, 0x03, 0x72, 0x06, 0x0B, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x0C, 0x0A, 0x01, 0x02, 0xA0, 0x82, 0x02, 0xB6, 0x30, 0x82, 0x02, 0xB2, 0x30, 0x1C, 0x06, 0x0A, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x0C, 0x01, 0x03, 0x30, 
			0x0E, 0x04, 0x08, 0x67, 0xFE, 0x3A, 0x52, 0x75, 0xF3, 0x82, 0x1F, 0x02, 0x02, 0x07, 0xD0, 0x04, 0x82, 0x02, 0x90, 0x31, 0x6B, 0x00, 0xFA, 0x73, 0xE6, 0x8D, 0x3D, 0x62, 0x93, 0x41, 0xA1, 0x44, 0x04, 0x17, 0x8D, 0x66, 0x7A, 0x75, 0x14, 0x89, 0xA8, 0xD1, 0x4D, 0x2A, 0xD7, 0x20, 0x27, 0x71, 0x58, 0x81, 0x16, 0xB5, 0xA6, 0x41, 0x75, 0x92, 0xB2, 0xF4, 0x0C, 0xAA, 0x9B, 0x00, 0x46, 0x85, 0x85, 0x3B, 0x09, 0x2A, 0x62, 0x33, 0x3F, 0x3D, 0x06, 0xC7, 0xE7, 0x16, 0x0C, 0xA7, 0x1D, 0x9C, 0xDA, 0x9D, 0xD3, 0xC9, 0x05, 0x60, 0xA5, 0xBE, 0xF0, 0x07, 0xD5, 0xA9, 0x4F, 0x8A, 0x80, 0xF8, 0x55, 0x7B, 0x7B, 0x3C, 
			0xA0, 0x7C, 0x29, 0x29, 0xAB, 0xB1, 0xE1, 0x5A, 0x25, 0xE3, 0x23, 0x6A, 0x56, 0x98, 0x37, 0x68, 0xAF, 0x9C, 0x87, 0xBB, 0x21, 0x6E, 0x68, 0xBE, 0xAE, 0x65, 0x0C, 0x41, 0x8F, 0x5C, 0x3A, 0xB8, 0xB1, 0x9D, 0x42, 0x37, 0xE4, 0xA0, 0x37, 0xA6, 0xB8, 0xAC, 0x85, 0xD7, 0x85, 0x27, 0x68, 0xD0, 0xB6, 0x3D, 0xC7, 0x39, 0x92, 0x41, 0x46, 0x24, 0xDD, 0x08, 0x57, 0x22, 0x6A, 0xC0, 0xB7, 0xAD, 0x52, 0xC6, 0x7F, 0xE5, 0x74, 0x6A, 0x5E, 0x28, 0xA3, 0x85, 0xBD, 0xE8, 0xAD, 0x5D, 0xA3, 0x55, 0xE6, 0x63, 0x15, 0x56, 0x7B, 0x01, 0x26, 0x68, 0x5F, 0x11, 0xA3, 0x12, 0x37, 0x02, 0xA5, 0xD0, 0xB7, 0x73, 0x0C, 0x7C, 
			0x97, 0xE1, 0xC6, 0x2F, 0x98, 0x82, 0x67, 0x2F, 0x5F, 0x3F, 0xBE, 0x32, 0x16, 0x25, 0x9D, 0x51, 0x48, 0x32, 0xCB, 0x42, 0xD1, 0x31, 0x07, 0xBE, 0x5D, 0xF8, 0xCD, 0x2C, 0x38, 0x0A, 0x33, 0x3B, 0x7B, 0x04, 0x84, 0xAE, 0x9C, 0xA7, 0x6B, 0x36, 0x39, 0x12, 0x87, 0x9D, 0x5B, 0x56, 0x00, 0x44, 0x11, 0xB1, 0xE2, 0x78, 0x14, 0x60, 0xF3, 0xE4, 0x1A, 0x08, 0x14, 0xC0, 0x9E, 0x49, 0x9F, 0xE0, 0x4C, 0xEC, 0x95, 0x15, 0x18, 0x48, 0x0E, 0xB9, 0x0B, 0x3A, 0xFE, 0x45, 0xB0, 0x2D, 0x0D, 0x4F, 0x94, 0x5A, 0x3C, 0x43, 0xB7, 0x40, 0x8E, 0x7B, 0xA2, 0x8E, 0x23, 0x9F, 0x75, 0x97, 0xE7, 0x21, 0x0D, 0xEB, 0xA3, 0x9D, 
			0x6C, 0xC0, 0xDC, 0x73, 0xED, 0x15, 0x98, 0xE3, 0xE8, 0x32, 0x2C, 0x12, 0x92, 0x45, 0x25, 0x45, 0x76, 0x18, 0xF5, 0x97, 0x7F, 0xAC, 0xCE, 0xCF, 0x23, 0xF7, 0xD1, 0xCF, 0x06, 0xAB, 0x82, 0x96, 0x1F, 0xF8, 0x68, 0x4F, 0x5D, 0xE1, 0x09, 0xAA, 0xCB, 0xB3, 0x50, 0x85, 0x46, 0x72, 0x14, 0x6C, 0x49, 0x84, 0x57, 0x55, 0x00, 0x78, 0x3E, 0xD9, 0xAA, 0xBD, 0xCC, 0xE2, 0x7B, 0x18, 0xAA, 0x2E, 0x5D, 0xB9, 0x28, 0xEA, 0x8F, 0x8C, 0xFA, 0xB7, 0x06, 0x27, 0x07, 0x89, 0x41, 0x3F, 0x66, 0x1A, 0x91, 0xCA, 0xE9, 0xEC, 0x09, 0x12, 0x1C, 0x67, 0xB2, 0x2A, 0x8B, 0x4A, 0xF0, 0x97, 0x17, 0xDC, 0x3E, 0xCD, 0x9F, 0x03, 
			0x15, 0xEF, 0x03, 0x84, 0x08, 0x4A, 0x73, 0xAE, 0xE4, 0x07, 0x30, 0x27, 0xF7, 0x25, 0x69, 0x9D, 0x6C, 0x7D, 0x81, 0x88, 0xCC, 0xFA, 0xD4, 0xC7, 0x64, 0x11, 0xC0, 0xC8, 0x2C, 0x23, 0xF6, 0xFF, 0x9B, 0xE3, 0xC8, 0x89, 0x85, 0x0B, 0x3E, 0x81, 0xD8, 0x9C, 0xBD, 0xD0, 0x2D, 0xCD, 0x15, 0xA9, 0x30, 0x84, 0xF7, 0x6D, 0xEF, 0x62, 0x3B, 0xA7, 0x8C, 0xC2, 0x93, 0x90, 0x6F, 0x91, 0xB4, 0x8A, 0x71, 0x4E, 0x41, 0x4E, 0x5C, 0x67, 0xB5, 0x49, 0xF8, 0x56, 0x3A, 0x83, 0x03, 0x4F, 0xB1, 0xF6, 0xB7, 0x31, 0x5B, 0x68, 0x26, 0x70, 0x89, 0xB1, 0x1E, 0x67, 0x4F, 0xBA, 0xE7, 0xD9, 0xDF, 0x91, 0xD8, 0xFB, 0x8A, 0xDD, 
			0xB2, 0xD3, 0x4B, 0xBB, 0x9F, 0x5C, 0xA3, 0x04, 0x2C, 0x87, 0xBC, 0xD5, 0xBE, 0x8C, 0xD7, 0xCF, 0x9B, 0x72, 0x82, 0xA6, 0x99, 0xDA, 0xD7, 0x66, 0x48, 0xE7, 0x8F, 0xE9, 0x48, 0x56, 0x9D, 0xD2, 0xB9, 0x28, 0x84, 0x4F, 0x6A, 0x83, 0xB2, 0xB9, 0x4D, 0x91, 0x10, 0x58, 0x22, 0x4C, 0xE7, 0x9D, 0xC6, 0x0C, 0x74, 0xF4, 0x16, 0x58, 0x30, 0xB7, 0xB7, 0x96, 0x39, 0x6C, 0x5D, 0xFA, 0xB2, 0x03, 0x8C, 0x98, 0xD2, 0xC0, 0x64, 0xB8, 0x05, 0x29, 0x4F, 0xF0, 0x4C, 0x43, 0x48, 0xD3, 0xD8, 0xBD, 0xC7, 0xC1, 0xEA, 0x39, 0x2A, 0xDF, 0xD4, 0xDA, 0x79, 0x7C, 0xB9, 0x06, 0xC7, 0x10, 0x8D, 0x8B, 0xF1, 0xA8, 0x8E, 0x44, 
			0x9E, 0x99, 0xFF, 0x81, 0x84, 0x8F, 0xD0, 0x38, 0xE1, 0xF0, 0x5A, 0x12, 0x5F, 0xC5, 0xA6, 0xED, 0x6D, 0xEE, 0xE7, 0x69, 0xC0, 0xA2, 0xB4, 0x13, 0xCA, 0x7A, 0x5D, 0xDE, 0x88, 0x75, 0xE7, 0xE2, 0x6D, 0x8A, 0xEC, 0x0F, 0x88, 0x3F, 0xE2, 0xCB, 0x60, 0xF0, 0x6A, 0xEC, 0xD0, 0xF4, 0x0D, 0x11, 0xC2, 0x84, 0x19, 0x67, 0x52, 0xAD, 0xC0, 0xC0, 0x20, 0x84, 0x6D, 0x7D, 0xEA, 0xD2, 0xF9, 0x3F, 0xE5, 0x58, 0x00, 0xED, 0x24, 0xD6, 0x50, 0x9B, 0x80, 0x80, 0x0A, 0x31, 0x81, 0xA8, 0x30, 0x0D, 0x06, 0x09, 0x2B, 0x06, 0x01, 0x04, 0x01, 0x82, 0x37, 0x11, 0x02, 0x31, 0x00, 0x30, 0x13, 0x06, 0x09, 0x2A, 0x86, 0x48, 
			0x86, 0xF7, 0x0D, 0x01, 0x09, 0x15, 0x31, 0x06, 0x04, 0x04, 0x01, 0x00, 0x00, 0x00, 0x30, 0x17, 0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x09, 0x14, 0x31, 0x0A, 0x1E, 0x08, 0x00, 0x4D, 0x00, 0x6F, 0x00, 0x6E, 0x00, 0x6F, 0x30, 0x69, 0x06, 0x09, 0x2B, 0x06, 0x01, 0x04, 0x01, 0x82, 0x37, 0x11, 0x01, 0x31, 0x5C, 0x1E, 0x5A, 0x00, 0x4D, 0x00, 0x69, 0x00, 0x63, 0x00, 0x72, 0x00, 0x6F, 0x00, 0x73, 0x00, 0x6F, 0x00, 0x66, 0x00, 0x74, 0x00, 0x20, 0x00, 0x52, 0x00, 0x53, 0x00, 0x41, 0x00, 0x20, 0x00, 0x53, 0x00, 0x43, 0x00, 0x68, 0x00, 0x61, 0x00, 0x6E, 0x00, 0x6E, 0x00, 0x65, 0x00, 0x6C, 
			0x00, 0x20, 0x00, 0x43, 0x00, 0x72, 0x00, 0x79, 0x00, 0x70, 0x00, 0x74, 0x00, 0x6F, 0x00, 0x67, 0x00, 0x72, 0x00, 0x61, 0x00, 0x70, 0x00, 0x68, 0x00, 0x69, 0x00, 0x63, 0x00, 0x20, 0x00, 0x50, 0x00, 0x72, 0x00, 0x6F, 0x00, 0x76, 0x00, 0x69, 0x00, 0x64, 0x00, 0x65, 0x00, 0x72, 0x30, 0x82, 0x02, 0xB7, 0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x07, 0x06, 0xA0, 0x82, 0x02, 0xA8, 0x30, 0x82, 0x02, 0xA4, 0x02, 0x01, 0x00, 0x30, 0x82, 0x02, 0x9D, 0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x07, 0x01, 0x30, 0x1C, 0x06, 0x0A, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x0C, 0x01, 
			0x06, 0x30, 0x0E, 0x04, 0x08, 0xB8, 0x22, 0xEA, 0x3C, 0x70, 0x6A, 0xFC, 0x39, 0x02, 0x02, 0x07, 0xD0, 0x80, 0x82, 0x02, 0x70, 0x76, 0xBE, 0x5B, 0xD5, 0x3D, 0x05, 0xC1, 0xDB, 0x10, 0xA3, 0x02, 0xBB, 0x7F, 0x0A, 0x8B, 0x54, 0xC1, 0x7D, 0x19, 0xDA, 0x7E, 0x82, 0xDF, 0xAD, 0x6B, 0x42, 0xC2, 0x95, 0x95, 0x00, 0x6E, 0x82, 0x77, 0xD5, 0x42, 0x6E, 0x21, 0xA2, 0x95, 0xB4, 0x98, 0xF5, 0xDD, 0x18, 0x6F, 0xC4, 0xF3, 0xB6, 0x93, 0xA0, 0x6C, 0xF4, 0x34, 0x7A, 0x48, 0x72, 0x08, 0xB1, 0x28, 0x51, 0x54, 0x10, 0x7F, 0x35, 0xB2, 0xE5, 0x89, 0x5C, 0x0A, 0x14, 0x31, 0x1C, 0x9D, 0xA9, 0xE4, 0x94, 0x91, 0x28, 0x65, 
			0xC4, 0xE7, 0x5E, 0xA9, 0x37, 0x08, 0x3D, 0xB1, 0x16, 0x61, 0x9D, 0xA9, 0x44, 0x6F, 0x20, 0x0C, 0x55, 0xD7, 0xCC, 0x48, 0x82, 0x13, 0x5D, 0xE1, 0xBD, 0x9D, 0xCE, 0x64, 0x28, 0x6D, 0x69, 0x4E, 0x08, 0x53, 0x09, 0xE0, 0xCC, 0xA8, 0x79, 0x04, 0xCF, 0xFA, 0x35, 0x1C, 0xA6, 0x70, 0x37, 0x64, 0x70, 0x74, 0xF8, 0xD0, 0xC4, 0x34, 0x0F, 0x71, 0xEF, 0x57, 0xC2, 0x43, 0x7D, 0xFA, 0xE5, 0x1B, 0x8C, 0x15, 0xA5, 0x08, 0x60, 0x78, 0xAF, 0xDA, 0x36, 0xDF, 0x79, 0x2D, 0xD7, 0x54, 0x35, 0xD7, 0x8D, 0x99, 0xD5, 0x81, 0xEC, 0x6D, 0x9F, 0x2D, 0x5E, 0xF8, 0x48, 0x85, 0x50, 0x20, 0x7D, 0xBB, 0x16, 0x4E, 0x39, 0x64, 
			0xB7, 0xBC, 0xED, 0xA9, 0x6A, 0x7A, 0x06, 0x09, 0x6B, 0xBC, 0x2C, 0x5A, 0xE1, 0x4F, 0xD4, 0xA9, 0x82, 0x83, 0x5B, 0xBD, 0xCE, 0x14, 0x31, 0x89, 0x66, 0xB3, 0x9C, 0x31, 0x23, 0x00, 0x4B, 0x02, 0x34, 0x85, 0x30, 0x39, 0x77, 0x80, 0x5D, 0x72, 0x0A, 0xCE, 0x43, 0x2A, 0x1F, 0x02, 0x09, 0xAB, 0x2D, 0x46, 0x3A, 0x1C, 0xD2, 0x7B, 0xF6, 0x02, 0x92, 0xCA, 0xDA, 0x26, 0x0C, 0xF8, 0xE2, 0x67, 0x7E, 0xE2, 0x55, 0xB1, 0x3F, 0x6A, 0x06, 0x65, 0x6D, 0x74, 0x98, 0x59, 0xE2, 0x8A, 0x1E, 0x61, 0x03, 0x4D, 0xFC, 0x68, 0x31, 0x6A, 0xE7, 0xCF, 0x52, 0x88, 0x8E, 0x06, 0x97, 0x77, 0xB3, 0x20, 0x7E, 0x09, 0x5D, 0x3B, 
			0xAF, 0x56, 0xF4, 0xE8, 0x4C, 0x69, 0x09, 0xB9, 0x80, 0x38, 0xDC, 0x66, 0x2E, 0x06, 0xF6, 0xCB, 0x1F, 0x1B, 0xAD, 0x51, 0xFF, 0xFD, 0x38, 0x8D, 0x03, 0x90, 0xCF, 0x31, 0x01, 0x30, 0xEA, 0x48, 0x4C, 0xBB, 0x40, 0x87, 0x1D, 0x97, 0x6A, 0x56, 0x4C, 0xED, 0x07, 0x23, 0x45, 0x50, 0x2F, 0x56, 0xC9, 0x90, 0x79, 0x09, 0xC5, 0x45, 0xB9, 0xAD, 0x58, 0x2B, 0x4C, 0xA3, 0x01, 0xE0, 0x2D, 0xE5, 0x30, 0xBC, 0x54, 0xEC, 0x65, 0xB4, 0x79, 0x22, 0x7D, 0x15, 0xF6, 0x28, 0xCD, 0x84, 0x7E, 0x27, 0x95, 0xA1, 0xC7, 0x82, 0x6D, 0xFB, 0xDF, 0x03, 0xD9, 0x14, 0xFE, 0x0A, 0x06, 0x6F, 0x14, 0xFF, 0x8A, 0x27, 0x80, 0x36, 
			0xDC, 0xBA, 0xAE, 0xDD, 0x44, 0x15, 0xA5, 0x6E, 0x64, 0x73, 0xBD, 0xFB, 0xAE, 0x6D, 0x6F, 0x42, 0x96, 0xDF, 0x90, 0xE5, 0x6A, 0x9B, 0x05, 0xAE, 0xD5, 0x0A, 0x22, 0x88, 0xD6, 0x5D, 0x4C, 0x7B, 0xB1, 0x3A, 0xFC, 0x0C, 0x32, 0x02, 0xB1, 0x18, 0x0D, 0xAF, 0xE0, 0xFE, 0x7E, 0x07, 0x96, 0x85, 0xBB, 0xC8, 0x21, 0x68, 0x12, 0xD4, 0xC8, 0xBF, 0x91, 0x47, 0xE2, 0xF3, 0xA5, 0xA3, 0x86, 0xE6, 0x30, 0x42, 0xF5, 0xA9, 0xB9, 0x48, 0xCB, 0x18, 0xE6, 0x64, 0x3B, 0xE0, 0x8E, 0xC3, 0x03, 0x45, 0xA0, 0xED, 0x1A, 0x09, 0xFF, 0xB3, 0x99, 0x14, 0x5F, 0xDA, 0x90, 0x58, 0x61, 0x8E, 0xF7, 0x0A, 0x00, 0xC7, 0x44, 0xE7, 
			0x73, 0x78, 0xC4, 0x8B, 0x39, 0xCE, 0x70, 0x0E, 0x24, 0x03, 0x95, 0x94, 0x73, 0x76, 0x10, 0x7E, 0x4C, 0xFF, 0xCA, 0x49, 0x93, 0x89, 0xD4, 0x3E, 0x1A, 0x88, 0xCC, 0x48, 0xA7, 0x78, 0x2F, 0x83, 0x4F, 0x6C, 0x33, 0x55, 0xDD, 0x7F, 0x7D, 0x4D, 0xE5, 0xCD, 0x9C, 0x3D, 0x04, 0x1E, 0xC1, 0x9B, 0x6D, 0x7E, 0x7A, 0xAC, 0x93, 0x5E, 0x2B, 0xC3, 0x85, 0x36, 0x07, 0x66, 0xE8, 0xC9, 0xC0, 0xD1, 0x54, 0xF4, 0x4C, 0x6A, 0x02, 0x24, 0x9A, 0x7D, 0x10, 0xD9, 0x79, 0x94, 0x00, 0x64, 0x63, 0x36, 0xDC, 0x35, 0x0C, 0x8F, 0x79, 0xBA, 0xC7, 0x10, 0x76, 0xF8, 0x4A, 0xD3, 0x69, 0x95, 0x23, 0x89, 0x66, 0xC4, 0x5A, 0xE7, 
			0xCE, 0x21, 0xBC, 0xCB, 0xF2, 0x4F, 0x92, 0x33, 0xE7, 0x89, 0xD6, 0x23, 0xF7, 0x67, 0x5B, 0x20, 0xD9, 0xDA, 0x1A, 0xD1, 0xF6, 0x9E, 0x01, 0x83, 0x51, 0xAF, 0x35, 0x43, 0xDD, 0x3A, 0xAB, 0xCA, 0x0E, 0xED, 0x2E, 0x4D, 0x1E, 0x91, 0xCF, 0x2E, 0xA9, 0x4D, 0x08, 0xD9, 0x48, 0x30, 0x37, 0x30, 0x1F, 0x30, 0x07, 0x06, 0x05, 0x2B, 0x0E, 0x03, 0x02, 0x1A, 0x04, 0x14, 0xA2, 0xED, 0x05, 0x50, 0x89, 0x91, 0x1D, 0xEB, 0xF6, 0x57, 0x66, 0xAF, 0x70, 0x15, 0xDD, 0x1A, 0xA1, 0x94, 0xB7, 0xB2, 0x04, 0x14, 0x09, 0xE4, 0x0B, 0xEC, 0x1D, 0x93, 0x3E, 0x32, 0x94, 0x6A, 0x95, 0x36, 0xDD, 0xBA, 0x93, 0x9D, 0x75, 0xB6, 
			0x3E, 0xF5 };

		// wrap the object so that we can use the extensions and fail
		class AttachableNativeObject : ICMAttachmentBearer
		{
			INativeObject nativeObj;

			public AttachableNativeObject (INativeObject obj)
			{
				nativeObj = obj;
			}

			public IntPtr Handle
			{
				get { return nativeObj.Handle; }
			}
		}

		protected virtual bool Skip (Type type)
		{
			return Skip (type.Name) || SkipDueToAttribute (type);
		}

		protected virtual bool Skip (string nativeName)
		{
			if (nativeName.Contains ("`")) {
				nativeName = nativeName.Substring (0, nativeName.IndexOf ('`'));
			}
			if (nativeName.StartsWith ("CGPDF", StringComparison.Ordinal))  // all those types crash the app
				return true;
			switch (nativeName) {
			case "CFMachPort":
			case "CFMessagePort":
			case "DispatchIO": // no way to instantiate it
			case "DispatchSource":
			case "AudioConverter": // does crash the tests
			case "AudioFile": // does crash the tests
			case "CFHTTPAuthentication":
			case "CFHTTPStream":
			case "SystemSound": // does crash the tests
			case "MusicPlayer": // does crash the tests
			case "MusicTrack": // does crash the tests
			case "AUGraph": // does crash the tests
			case "CGFunction":
			case "CGShading":
			case "CVMetalTexture":
			case "CVMetalTextureCache":
			case "CTRun":
			case "CTRunDelegate":
			case "CGImageMetadata":
			case "SecKeyChain": // static class
			case "VTDecompressionSession":
			case "Class": // makes no sense to test
			case "Selector": // makes no sense to test
			case "CFRunLoopSource":
			case "CFRunLoop":
			case "NSZone":
			case "MusicSequence": // crashes tests
			case "AudioBuffers": // crashes tests
			case "CGContext":
			case "AudioComponent":
			case "AudioUnit":
			case "AURenderEventEnumerator":
			case "CFPropertyList":
			case "CGLayer":
			case "CMFormatDescription":
			case "CMAudioFormatDescription":
			case "CMVideoFormatDescription":
			case "CMBlockBuffer":
			case "CMSampleBuffer":
			case "CVBuffer": // DOES support the API, but it has its own version and is already in the bindings, so no need ATM
			case "CVImageBuffer": // same as CVBuffer
			case "CVPixelBuffer": // same as CVBuffer
			case "MTAudioProcessingTap":
			case "Protocol":
			case "MidiObject": // causes crash
			case "CMClockOrTimebase":
			case "MidiClient":
			case "MidiPort":
			case "MidiEntity":
			case "MidiDevice":
			case "MidiEndpoint":
			case "ABMultiValue":
			case "ABMutableMultiValue":
			case "SecProtocolMetadata": // Read-only object that is surfaced during TLS negotiation callbacks, can not be created from user code.
			case "SecProtocolOptions":  // Read-only object that is surfaced during TLS negotiation callbacks, can not be created from user code.
			case "NWError":               // Only ever surfaced, not created from usercode
			case "NWInterface":           // Only ever surfaced, not created from usercode
			case "NWPath":                // Only ever surfaced, not created from usercode
			case "NWProtocolStack":       // Only ever surfaced, not created from usercode
			case "NWProtocolMetadata":    // While technically it can be created and the header files claim the methods exists, the library is missing the methods (radar: 42443077)
			case "ABSource": // not skipped when running on iOS 6.1
			// type was removed in iOS 10 (and replaced) and never consumed by other API
			case "CGColorConverter":
				return true;
			default:
 				return false;
			}
		}

		protected INativeObject GetINativeInstance (Type t)
		{
			var ctor = t.GetConstructor (Type.EmptyTypes);
			if ((ctor != null) && !ctor.IsAbstract)
				return ctor.Invoke (null) as INativeObject;

			if (!NativeObjectInterfaceType.IsAssignableFrom (t))
				throw new ArgumentException ("t");
			switch (t.Name) {
			case "CFAllocator":
				return CFAllocator.SystemDefault;
			case "CFBundle":
				var bundles = CFBundle.GetAll ();
				if (bundles.Length > 0)
					return bundles [0];
				else
					throw new InvalidOperationException (string.Format ("Could not create the new instance for type {0}.", t.Name));
			case "CFNotificationCenter":
				return CFNotificationCenter.Darwin;
			case "CFReadStream":
			case "CFStream":
				CFReadStream readStream;
				CFWriteStream writeStream;
				CFStream.CreatePairWithSocketToHost ("www.google.com", 80, out readStream, out writeStream);
				return readStream;
			case "CFWriteStream":
				CFStream.CreatePairWithSocketToHost ("www.google.com", 80, out readStream, out writeStream);
				return writeStream;
			case "CFUrl":
				return CFUrl.FromFile ("/etc");
			case "CFPropertyList":
				return CFPropertyList.FromData (NSData.FromString ("<string>data</string>")).PropertyList;
			case "DispatchData":
				return DispatchData.FromByteBuffer (new byte [] { 1, 2, 3, 4 });
			case "AudioFile":
				var path = Path.GetFullPath ("1.caf");
				var af = AudioFile.Open (CFUrl.FromFile (path), AudioFilePermission.Read, AudioFileType.CAF);
				return af;
			case "CFHTTPMessage":
				return CFHTTPMessage.CreateEmpty (false);
			case "CGBitmapContext":
				byte[] data = new byte [400];
				using (CGColorSpace space = CGColorSpace.CreateDeviceRGB ()) {
					return new CGBitmapContext (data, 10, 10, 8, 40, space, CGBitmapFlags.PremultipliedLast);
				}
			case "CGContextPDF":
				var filename = Environment.GetFolderPath (Environment.SpecialFolder.CommonDocuments) + "/t.pdf";
				using (var url = new NSUrl (filename))
					return new CGContextPDF (url);
			case "CGColorConversionInfo":
				var cci = new GColorConversionInfoTriple () {
					Space = CGColorSpace.CreateGenericRgb (),
					Intent = CGColorRenderingIntent.Default,
					Transform = CGColorConversionInfoTransformType.ApplySpace
				};
				return new CGColorConversionInfo ((NSDictionary) null, cci, cci, cci);
			case "CGDataConsumer":
				using (NSMutableData destData = new NSMutableData ()) {
					return new CGDataConsumer (destData);
				}
			case "CGDataProvider":
				filename = "xamarin1.png";
				return new CGDataProvider (filename);
			case "CGFont":
				return CGFont.CreateWithFontName ("Courier New");
			case "CGPattern":
				return new CGPattern (
					new RectangleF (0, 0, 16, 16), 
					CGAffineTransform.MakeIdentity (), 
					16, 16, 
					CGPatternTiling.NoDistortion,
					true, 
					(cgc) => {});
			case "CMBufferQueue":
				return CMBufferQueue.CreateUnsorted (2);
			case "CTFont":
				CTFontDescriptorAttributes fda = new CTFontDescriptorAttributes () {
					FamilyName = "Courier",
					StyleName = "Bold",
					Size = 16.0f
				};
				using (var fd = new CTFontDescriptor (fda))
					return new CTFont (fd, 10);
			case "CTFontCollection":
				return new CTFontCollection (new CTFontCollectionOptions ());
			case "CTFontDescriptor":
				fda = new CTFontDescriptorAttributes ();
				return new CTFontDescriptor (fda);
			case "CTTextTab":
				return new CTTextTab (CTTextAlignment.Left, 2);
			case "CTTypesetter":
				return new CTTypesetter (new NSAttributedString ("Hello, world",
					new CTStringAttributes () {
						ForegroundColorFromContext =  true,
						Font = new CTFont ("ArialMT", 24)
					}));
			case "CTFrame":
				var framesetter = new CTFramesetter (new NSAttributedString ("Hello, world",
					new CTStringAttributes () {
						ForegroundColorFromContext =  true,
						Font = new CTFont ("ArialMT", 24)
					}));
				var bPath = UIBezierPath.FromRect (new RectangleF (0, 0, 3, 3));
				return framesetter.GetFrame (new NSRange (0, 0), bPath.CGPath, null);
			case "CTFramesetter":
				return new CTFramesetter (new NSAttributedString ("Hello, world",
					new CTStringAttributes () {
						ForegroundColorFromContext =  true,
						Font = new CTFont ("ArialMT", 24)
					}));
			case "CTGlyphInfo":
				return new CTGlyphInfo ("Zapfino", new CTFont ("ArialMT", 24), "Foo");
			case "CTLine":
				return new CTLine (new NSAttributedString ("Hello, world",
					new CTStringAttributes () {
						ForegroundColorFromContext =  true,
						Font = new CTFont ("ArialMT", 24)
					}));
			case "CGImageDestination":
				var storage = new NSMutableData ();
				return CGImageDestination.Create (new CGDataConsumer (storage), "public.png", 1);
			case "CGImageMetadataTag":
				using (NSString name = new NSString ("tagName"))
				using (var value = new NSString ("value"))
					return new CGImageMetadataTag (CGImageMetadataTagNamespaces.Exif, CGImageMetadataTagPrefixes.Exif, name, CGImageMetadataType.Default, value);
			case "CGImageSource":
				filename = "xamarin1.png";
				return CGImageSource.FromUrl (NSUrl.FromFilename (filename));
			case "SecPolicy":
				return SecPolicy.CreateSslPolicy (false, null);
			case "SecIdentity":
				using (var options = NSDictionary.FromObjectAndKey (new NSString ("farscape"), SecImportExport.Passphrase)) {
					NSDictionary[] array;
					var result = SecImportExport.ImportPkcs12 (farscape_pfx, options, out array);
					if (result != SecStatusCode.Success)
						throw new InvalidOperationException (string.Format ("Could not create the new instance for type {0} due to {1}.", t.Name, result));
					return new SecIdentity (array [0].LowlevelObjectForKey (SecImportExport.Identity.Handle));
				}
			case "SecTrust":
				X509Certificate x = new X509Certificate (mail_google_com);
				using (var policy = SecPolicy.CreateSslPolicy (true, "mail.google.com"))
					return new SecTrust (x, policy);
			case "SslContext":
				return new SslContext (SslProtocolSide.Client, SslConnectionType.Stream);
			case "UIFontFeature":
				return new UIFontFeature (CTFontFeatureNumberSpacing.Selector.ProportionalNumbers);
			case "NetworkReachability":
				return new NetworkReachability (IPAddress.Loopback, null);
			case "VTCompressionSession":
			case "VTSession":
				return VTCompressionSession.Create (1024, 768, CMVideoCodecType.H264, (sourceFrame, status, flags, buffer) => { }, null, (CVPixelBufferAttributes) null);
			case "VTFrameSilo":
				return VTFrameSilo.Create ();
			case "VTMultiPassStorage":
				return VTMultiPassStorage.Create ();
			case "CFString":
				return new CFString ("test");
			case "DispatchBlock":
				return new DispatchBlock (() => { });
			case "DispatchQueue":
				return new DispatchQueue ("com.example.subsystem.taskXYZ");
			case "DispatchGroup":
				return DispatchGroup.Create ();
			case "CGColorSpace":
				return CGColorSpace.CreateDeviceCmyk ();
			case "CGGradient":
				CGColor[] cArray = { UIColor.Black.CGColor, UIColor.Clear.CGColor, UIColor.Blue.CGColor };
				return new CGGradient (null, cArray);
			case "CGImage":
				filename = "xamarin1.png";
				using (var dp = new CGDataProvider (filename))
					return CGImage.FromPNG (dp, null, false, CGColorRenderingIntent.Default);
			case "CGColor":
				return UIColor.Black.CGColor;
			case "CMClock":
				CMClockError ce;
				CMClock clock = CMClock.CreateAudioClock (out ce);
				if (ce == CMClockError.None)
					return clock;
				throw new InvalidOperationException (string.Format ("Could not create the new instance for type {0}.", t.Name));
			case "CMTimebase":
				clock = CMClock.CreateAudioClock (out ce);
				if (ce == CMClockError.None) {
					return new CMTimebase (clock);
				}
				throw new InvalidOperationException (string.Format ("Could not create the new instance for type {0}.", t.Name));
			case "CVPixelBufferPool":
				return new CVPixelBufferPool (
					new CVPixelBufferPoolSettings (),
					new CVPixelBufferAttributes (CVPixelFormatType.CV24RGB, 100, 50)
				);
			case "NWAdvertiseDescriptor":
				return NWAdvertiseDescriptor.CreateBonjourService ("sampleName" + DateTime.Now, "_nfs._tcp");
			case "NWConnection": {
				var endpoint = NWEndpoint.Create ("www.microsoft.com", "https");
				var parameters = NWParameters.CreateTcp (configureTcp: null);
				return new NWConnection (endpoint, parameters);
			}
			case "NWContentContext":
				return new NWContentContext ("contentContext" + DateTime.Now);
			case "NWEndpoint":
				return NWEndpoint.Create ("www.microsoft.com", "https");
			case "NWListener":
				return NWListener.Create (NWParameters.CreateTcp (configureTcp: null));
			case "NWParameters":
				return NWParameters.CreateTcp (configureTcp: null);
			case "NWProtocolDefinition":
				// Makes a new instance every time
				return NWProtocolDefinition.TcpDefinition;
			case "NWProtocolOptions":
				return NWProtocolOptions.CreateTcp ();
			case "SecCertificate":
				using (var cdata = NSData.FromArray (mail_google_com))
					return new SecCertificate (cdata);
			case "SecCertificate2":
				using (var cdata = NSData.FromArray (mail_google_com))
					return new SecCertificate2 (new SecCertificate (cdata));
			case "SecTrust2":
				X509Certificate x2 = new X509Certificate (mail_google_com);
				using (var policy = SecPolicy.CreateSslPolicy (true, "mail.google.com"))
					return new SecTrust2 (new SecTrust (x2, policy));
			case "SecIdentity2":
				using (var options = NSDictionary.FromObjectAndKey (new NSString ("farscape"), SecImportExport.Passphrase)) {
					NSDictionary[] array;
					var result = SecImportExport.ImportPkcs12 (farscape_pfx, options, out array);
					if (result != SecStatusCode.Success)
						throw new InvalidOperationException (string.Format ("Could not create the new instance for type {0} due to {1}.", t.Name, result));
					return new SecIdentity2 (new SecIdentity (array [0].LowlevelObjectForKey (SecImportExport.Identity.Handle)));
				}
				
			case "SecKey":
				SecKey private_key;
				SecKey public_key;
				using (var record = new SecRecord (SecKind.Key)) {
					record.KeyType = SecKeyType.RSA;
					record.KeySizeInBits = 512; // it's not a performance test :)
					SecKey.GenerateKeyPair (record.ToDictionary (), out public_key, out private_key);
					return private_key;
				}
			case "SecAccessControl":
				return new SecAccessControl (SecAccessible.WhenPasscodeSetThisDeviceOnly);
			default:
				throw new InvalidOperationException (string.Format ("Could not create the new instance for type {0}.", t.Name));
			}
		}

		protected ICMAttachmentBearer GetInstance (Type t)
		{
			if (!CMAttachmentInterfaceType.IsAssignableFrom (t))
				throw new ArgumentException ("t");
			switch (t.Name) {
			case "CMBlockBuffer":
				CMBlockBufferError bbe;
				var result = CMBlockBuffer.CreateEmpty (0, CMBlockBufferFlags.AssureMemoryNow, out bbe);
				if (bbe == CMBlockBufferError.None)
					return result;
				else
					throw new InvalidOperationException (string.Format ("Could not create the new instance {0}.", bbe.ToString ()));
			case "CMSampleBuffer":
				var pixelBuffer = new CVPixelBuffer (20, 10, CVPixelFormatType.CV24RGB);

				CMFormatDescriptionError fde;
				var desc = CMVideoFormatDescription.CreateForImageBuffer (pixelBuffer, out fde);

				var sampleTiming = new CMSampleTimingInfo ();

				CMSampleBufferError sbe;
				var sb = CMSampleBuffer.CreateForImageBuffer (pixelBuffer, true, desc, sampleTiming, out sbe);
				if (sbe == CMSampleBufferError.None)
					return sb;
				else
					throw new InvalidOperationException (string.Format ("Could not create the new instance {0}.", sbe.ToString ()));
			default:
				throw new InvalidOperationException (string.Format ("Could not create the new instance for type {0}.", t.Name));
			}
		}

		[Test]
		// test that the tag classes DO support the attachments API.
		public void CheckAttachments ()
		{
			var types = CMClockType.Assembly.GetTypes ()
				.Where(t => CMAttachmentInterfaceType.IsAssignableFrom(t) && !t.IsInterface);
			foreach (var t in types) {
				ICMAttachmentBearer obj = GetInstance (t);
				if (obj is NSObject)
					continue;
				Assert.That (obj.Handle, Is.Not.EqualTo (IntPtr.Zero), t.Name + ".Handle");
				using (var attch = new CFString ("myAttch")) {
					var mode = CMAttachmentMode.ShouldNotPropagate;
					CMAttachmentMode otherMode;
					obj.SetAttachment ("key", attch, CMAttachmentMode.ShouldNotPropagate);
					using (var otherAttch = obj.GetAttachment<CFString> ("key", out otherMode)) {
						obj.RemoveAllAttachments ();
						Assert.AreEqual (mode, otherMode);
						Assert.IsNotNull (otherAttch, "For type {0}", t.Name);
						Assert.AreEqual (attch.ToString (), otherAttch.ToString (), "For type {0}", t.Name);
					}
				}
				if (t is IDisposable) {
					var disposable = obj as IDisposable;
					disposable.Dispose ();
				}
			}
		}

		[Test]
		// test that the classes do not support the attachments API
		public void CheckFailAttachments ()
		{
			// get all tupes that are public, native object but not NSobjects or DispatchSources and that are not interfaces or abstract classes
			var types = CMClockType.Assembly.GetTypes ()
				.Where(t => !t.IsNotPublic && !CMAttachmentInterfaceType.IsAssignableFrom(t)
					&& NativeObjectInterfaceType.IsAssignableFrom (t) && !t.IsSubclassOf (NSObjectType)
					&& !t.IsSubclassOf (DispatchSourceType) && !t.IsInterface && !t.IsAbstract);
			foreach (var t in types) {
				if (Skip (t))
					continue; 
				var obj = new AttachableNativeObject (GetINativeInstance (t));
				Assert.That (obj.Handle, Is.Not.EqualTo (IntPtr.Zero), t.Name + ".Handle");
				using (var attch = new CFString ("myAttch")) {
					CMAttachmentMode otherMode;
					obj.SetAttachment ("key", attch, CMAttachmentMode.ShouldNotPropagate);
					using (var otherAttch = obj.GetAttachment<CFString> ("key", out otherMode)) {
						obj.RemoveAllAttachments ();
						Assert.Null (otherAttch, "For type {0}", t.Name);
					}
				}
				if (t is IDisposable) {
					var disposable = obj as IDisposable;
					disposable.Dispose ();
				}
			}
		}
	}
}

#endif // !__WATCHOS__
