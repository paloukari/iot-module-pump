// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;

namespace Microsoft.Azure.Devices.Client.HsmAuthentication
{
    internal static class HttpClientHelper
    {
        private const string HttpScheme = "http";
        private const string HttpsScheme = "https";
        private const string UnixScheme = "unix";

        public static string GetBaseUrl(Uri providerUri)
        {
            if (providerUri.Scheme.Equals(UnixScheme, StringComparison.OrdinalIgnoreCase))
            {
                return $"{HttpScheme}://{providerUri.Segments.Last()}";
            }
            return providerUri.OriginalString;
        }
    }
}
