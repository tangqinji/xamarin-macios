//
// Unit tests for UIAccessibility
//
// Authors:
//	Sebastien Pouliot  <sebastien@xamarin.com>
//
// Copyright 2013 Xamarin Inc. All rights reserved.
//

#if !__WATCHOS__ && !MONOMAC

using System;
#if XAMCORE_2_0
using Foundation;
using UIKit;
using ObjCRuntime;
#else
using MonoTouch.Foundation;
using MonoTouch.ObjCRuntime;
using MonoTouch.UIKit;
#endif
using NUnit.Framework;

namespace MonoTouchFixtures.UIKit {

	[TestFixture]
	[Preserve (AllMembers = true)]
	public class AccessibilityTest {

		[Test]
		public void RequestGuidedAccessSession ()
		{
			TestRuntime.AssertSystemVersion (PlatformName.iOS, 7, 0, throwIfOtherPlatform: false);

			// should not affect execution since it needs to be a "supervised" device (and allowed in MDM)
			UIAccessibility.RequestGuidedAccessSession (true, delegate (bool didSuccess) {
				Assert.False (didSuccess, "devices are not supervised by default");
			});
			UIAccessibility.RequestGuidedAccessSession (false, null);
		}
	}
}

#endif // !__WATCHOS__
