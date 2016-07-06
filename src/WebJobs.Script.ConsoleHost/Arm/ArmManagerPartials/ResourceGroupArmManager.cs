﻿using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using WebJobs.Script.ConsoleHost.Arm.Extensions;
using WebJobs.Script.ConsoleHost.Arm.Models;
using WebJobs.Script.ConsoleHost.Common;

namespace WebJobs.Script.ConsoleHost.Arm
{
    public partial class ArmManager
    {
        public async Task<ArmArrayWrapper<object>> GetResourceGroupResources(ResourceGroup resourceGroup)
        {
            return await ArmHttp<ArmArrayWrapper<object>>(HttpMethod.Get, ArmUriTemplates.ResourceGroupResources.Bind(resourceGroup));
        }
        public async Task<ResourceGroup> Load(ResourceGroup resourceGroup)
        {

            resourceGroup.FunctionsApps = resources.value
                .Where(r => r.type.Equals(Constants.WebAppArmType, StringComparison.OrdinalIgnoreCase) &&
                            r.kind?.Equals(Constants.FunctionAppArmKind, StringComparison.OrdinalIgnoreCase) == true)
                .Select(r => new Site(resourceGroup.SubscriptionId, resourceGroup.ResourceGroupName, r.name));



            return resourceGroup;
        }

        public async Task<ResourceGroup> CreateResourceGroup(ResourceGroup resourceGroup)
        {
            await ArmHttp(HttpMethod.Put, ArmUriTemplates.ResourceGroup.Bind(resourceGroup), new { properties = new { }, location = resourceGroup.Location });
            return resourceGroup;
        }

        public async Task<ResourceGroup> EnsureResourceGroup(ResourceGroup resourceGroup)
        {
            var rgResponse = await _client.HttpInvoke(HttpMethod.Get, ArmUriTemplates.ResourceGroup.Bind(resourceGroup));

            return rgResponse.IsSuccessStatusCode
                ? resourceGroup
                : await CreateResourceGroup(resourceGroup);
        }
    }
}