// Copyright 2011 Xamarin Inc. All rights reserved

#if !__TVOS__ && !__WATCHOS__ && !MONOMAC

using System;
using System.Drawing;
#if XAMCORE_2_0
using Foundation;
using UIKit;
#else
using MonoTouch.Foundation;
using MonoTouch.UIKit;
#endif
using NUnit.Framework;

#if XAMCORE_2_0
using RectangleF=CoreGraphics.CGRect;
using SizeF=CoreGraphics.CGSize;
using PointF=CoreGraphics.CGPoint;
#else
using nfloat=global::System.Single;
using nint=global::System.Int32;
using nuint=global::System.UInt32;
#endif

namespace MonoTouchFixtures.UIKit {
	
	[TestFixture]
	[Preserve (AllMembers = true)]
	public class WebViewTest {

		[TestFixtureSetUp]
		public void Setup ()
		{
			if (Type.GetType ("UIKit.DeprecatedWebView, Xamarin.iOS") != null)
				Assert.Ignore ("All type references to UIWebView were removed (optimized).");
		}
		
		[Test]
		public void InitWithFrame ()
		{
			RectangleF frame = new RectangleF (10, 10, 100, 100);
			using (UIWebView wv = new UIWebView (frame)) {
				Assert.That (wv.Frame, Is.EqualTo (frame), "Frame");
				Assert.Null (wv.Request, "Request");
			}
		}

		[Test]
		public void InstantiateDerivedClass ()
		{
			using (var derived = new DerivedUIWebView ()) {
				// bug #9261 - just instantiating a derived UIWebView class crashes on iOS 5
			}
		}

		class DerivedUIWebView : UIWebView 
		{
		}
	}

}

#endif // !__TVOS__ && !__WATCHOS__
