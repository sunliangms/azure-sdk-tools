﻿// ----------------------------------------------------------------------------------
//
// Copyright Microsoft Corporation
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ----------------------------------------------------------------------------------

namespace Microsoft.Azure.Commands.ManagedCache
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using Microsoft.Azure.Management.ManagedCache;
    using Microsoft.Azure.Management.ManagedCache.Models;
    using Microsoft.WindowsAzure;
    using Microsoft.WindowsAzure.Commands.Utilities.Common;
    using Microsoft.Azure.Commands.ManagedCache.Models;

    class PSCacheClient
    {
        private const string CacheResourceType = "Caching";
        private const string CacheResourceProviderNamespace = "cacheservice";
        private const string CacheServiceReadyState = "Active";

        private ManagedCacheClient client;
        public PSCacheClient(WindowsAzureSubscription currentSubscription)
        {
            client = currentSubscription.CreateClient<ManagedCacheClient>();
        }
        public PSCacheClient() { }
        public Action<string> ProgressRecorder { get; set; }

        public CloudServiceGetResponse.Resource CreateCacheService (
            string subscriptionID,
            string cacheServiceName, 
            string location, 
            string sku, 
            string memorySize)
        {
            WriteProgress(Properties.Resources.InitializingCacheParameters);
            CacheServiceCreateParameters param = InitializeParameters(location, sku, memorySize);

            WriteProgress(Properties.Resources.CreatingPrerequisites);
            string cloudServiceName = EnsureCloudServiceExists(subscriptionID, location);

            WriteProgress(Properties.Resources.VerifyingCacheServiceName);
            if (!(client.CacheServices.CheckNameAvailability(cloudServiceName,cacheServiceName).Available))
            {
                throw new ArgumentException(Properties.Resources.CacheServiceNameUnavailable);
            }

            WriteProgress(Properties.Resources.CreatingCacheService);
            client.CacheServices.CreateCacheService(cloudServiceName, cacheServiceName, param);

            WriteProgress(Properties.Resources.WaitForCacheServiceReady);
            CloudServiceGetResponse.Resource cacheResource = WaitForProvisionDone(cacheServiceName, cloudServiceName);

            return cacheResource;
        }

        private static CacheServiceCreateParameters InitializeParameters(string location, string sku, string memorySize)
        {
            CacheServiceCreateParameters param = new CacheServiceCreateParameters();
            IntrinsicSettings settings = new IntrinsicSettings();
            IntrinsicSettings.CacheServiceInput input = new IntrinsicSettings.CacheServiceInput();
            settings.CacheServiceInputSection = input;
            param.Settings = settings;

            const int CacheMemoryObjectSize = 1024;
            if (string.IsNullOrEmpty(sku))
            {
                sku = "Basic";
            }
            Models.CacheSkuCountConvert convert = new Models.CacheSkuCountConvert(sku);
            input.Location = location;
            input.SkuCount = convert.ToSkuCount(memorySize);
            input.ServiceVersion = "1.0.0";
            input.ObjectSizeInBytes = CacheMemoryObjectSize;
            input.SkuType = sku;
            return param;
        }

        private CloudServiceGetResponse.Resource WaitForProvisionDone(string cacheServiceName, string cloudServiceName)
        {
            CloudServiceGetResponse.Resource cacheResource = null;
            //Service state goes through Creating/Updating to Active. We only care about active
            int waitInMinutes = 30;
            while (waitInMinutes > 0)
            {
                cacheResource = GetCacheService(cloudServiceName, cacheServiceName);
                if (cacheResource.SubState == CacheServiceReadyState)
                {
                    break;
                }
                else
                {
                    const int milliSecondPerMinute = 60000;
                    Thread.Sleep(milliSecondPerMinute);
                    waitInMinutes--;
                }
            }

            if (waitInMinutes < 0)
            {
                throw new InvalidOperationException(Properties.Resources.TimeoutWaitForCacheServiceReady);
            }
            return cacheResource;
        }

        public string NormalizeCacheServiceName(string cacheServiceName)
        {
            //Cache serice can only take lower case. We help people to get it right
            cacheServiceName = cacheServiceName.ToLower();

            //Now Check length and pattern
            int length = cacheServiceName.Length;
            if (length < 6 || length > 22 || !Regex.IsMatch(cacheServiceName,"^[a-zA-Z][a-zA-Z0-9]*$"))
            {
                throw new ArgumentException(Properties.Resources.InvalidCacheServiceName);
            }

            return cacheServiceName;
        }

        public void DeleteCacheService(string cacheServiceName)
        {
            string cloudServiceName = GetAssociatedCloudServiceName(cacheServiceName);
            if (string.IsNullOrEmpty(cloudServiceName))
            {
                string error = string.Format(Properties.Resources.CacheServiceNotExisting, cacheServiceName);
                throw new ArgumentException(error);
            }
            client.CacheServices.Delete(cloudServiceName, cacheServiceName);
        }

        public string EnsureCloudServiceExists(string subscriptionId,  string location)
        {
            string cloudServiceName = GetCloudServiceName(subscriptionId, location);

            if (!CloudServiceExists(cloudServiceName))
            {
                CloudServiceCreateParameters parameters = new CloudServiceCreateParameters();
                parameters.GeoRegion = location;
                parameters.Description = cloudServiceName;
                parameters.Label = cloudServiceName;
                OperationResponse response = client.CloudServices.Create(cloudServiceName, parameters);
            }
            return cloudServiceName;
        }

        public List<PSCacheService> GetCacheServices(string cacheServiceName)
        {
            List<PSCacheService> services = new List<PSCacheService>();
            CloudServiceListResponse listResponse = client.CloudServices.List(); 
            foreach (CloudServiceListResponse.CloudService cloudService in listResponse)
            {
                foreach(CloudServiceListResponse.CloudService.AddOnResource resource in cloudService.Resources)
                {
                    if (resource.Type == CacheResourceType &&
                            (string.IsNullOrEmpty(cacheServiceName) ||
                             cacheServiceName.Equals(resource.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        services.Add(new PSCacheService(resource));
                    }
                }
            }
            return services;
        }

        private string GetAssociatedCloudServiceName(string cacheServiceName)
        {
            CloudServiceListResponse listResponse = client.CloudServices.List();
            foreach (CloudServiceListResponse.CloudService cloudService in listResponse)
            {
                CloudServiceListResponse.CloudService.AddOnResource matched = cloudService.Resources.FirstOrDefault(
                   resource => { 
                       return resource.Type == CacheResourceType 
                        && cacheServiceName.Equals(resource.Name, StringComparison.OrdinalIgnoreCase);
                   });

                if (matched!=null)
                {
                    return cloudService.Name;
                }
            }
            return null;
        }

        private CloudServiceGetResponse.Resource GetCacheService(string cloudServiceName, string cacheServiceName)
        {
            CloudServiceGetResponse response = client.CloudServices.Get(cloudServiceName);
            CloudServiceGetResponse.Resource cacheResource = response.Resources.FirstOrDefault((r) => 
            { 
                return r.Type == CacheResourceType && r.Name.Equals(cacheServiceName, StringComparison.OrdinalIgnoreCase);
            });
            return cacheResource;
        }

        private bool CloudServiceExists(string cloudServiceName)
        {
            try
            {
                CloudServiceGetResponse response = client.CloudServices.Get(cloudServiceName);
            }
            catch (CloudException ex)
            {
                if (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return false;
                }
                throw ex;
            }
            return true;
        }

        private void WriteProgress(string progress)
        {
            if (ProgressRecorder!=null)
            {
                ProgressRecorder(progress);
            }
        }

        /// <summary>
        /// The following logic was ported from Azure Cache management portal. It is critical to maintain the 
        /// parity. Do not modify unless you understand the consequence.
        /// </summary>
        private string GetCloudServiceName(string subscriptionId, string region)
        {
            string hashedSubId = string.Empty;
            string extensionPrefix = CacheResourceType;
            using (SHA256 sha256 = SHA256Managed.Create())
            {
                hashedSubId = Base32NoPaddingEncode(sha256.ComputeHash(UTF8Encoding.UTF8.GetBytes(subscriptionId)));
            }

            return string.Format(CultureInfo.InvariantCulture, "{0}{1}-{2}", extensionPrefix, hashedSubId, region.Replace(' ', '-'));
        }

        private string Base32NoPaddingEncode(byte[] data)
        {
            const string Base32StandardAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

            StringBuilder result = new StringBuilder(Math.Max((int)Math.Ceiling(data.Length * 8 / 5.0), 1));

            byte[] emptyBuffer = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 };
            byte[] workingBuffer = new byte[8];

            // Process input 5 bytes at a time
            for (int i = 0; i < data.Length; i += 5)
            {
                int bytes = Math.Min(data.Length - i, 5);
                Array.Copy(emptyBuffer, workingBuffer, emptyBuffer.Length);
                Array.Copy(data, i, workingBuffer, workingBuffer.Length - (bytes + 1), bytes);
                Array.Reverse(workingBuffer);
                ulong val = BitConverter.ToUInt64(workingBuffer, 0);

                for (int bitOffset = ((bytes + 1) * 8) - 5; bitOffset > 3; bitOffset -= 5)
                {
                    result.Append(Base32StandardAlphabet[(int)((val >> bitOffset) & 0x1f)]);
                }
            }

            return result.ToString();
        } 
    }
}