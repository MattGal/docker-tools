﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.DotNet.ImageBuilder.Models.Image;

namespace Microsoft.DotNet.ImageBuilder
{
    public static class ImageInfoHelper
    {
        public static void MergeRepos(RepoData[] srcRepos, List<RepoData> targetRepos, ImageInfoMergeOptions options = null)
        {
            if (options == null)
            {
                options = new ImageInfoMergeOptions();
            }

            foreach (RepoData srcRepo in srcRepos)
            {
                RepoData targetRepo = targetRepos.FirstOrDefault(r => r.Repo == srcRepo.Repo);
                if (targetRepo == null)
                {
                    targetRepos.Add(srcRepo);
                }
                else
                {
                    MergeData(srcRepo, targetRepo, options);
                }
            }
        }

        private static void MergeData(object srcObj, object targetObj, ImageInfoMergeOptions options)
        {
            if (srcObj.GetType() != targetObj.GetType())
            {
                throw new ArgumentException("Object types don't match.", nameof(targetObj));
            }

            foreach (PropertyInfo property in srcObj.GetType().GetProperties())
            {
                if (property.PropertyType == typeof(string))
                {
                    property.SetValue(targetObj, property.GetValue(srcObj));
                }
                else if (typeof(IDictionary).IsAssignableFrom(property.PropertyType))
                {
                    MergeDictionaries(property, srcObj, targetObj, options);
                }
                else if (typeof(IList<string>).IsAssignableFrom(property.PropertyType))
                {
                    if (srcObj is ImageData &&
                        property.Name == nameof(ImageData.SimpleTags) &&
                        options.ReplaceTags)
                    {
                        // SimpleTags can be merged or replaced depending on the scenario.
                        // When merging multiple image info files together into a single file, the tags should be
                        // merged to account for cases where tags for a given image are spread across multiple
                        // image info files.  But when publishing an image info file it the source tags should replace
                        // the destination tags.  Any of the image's tags contained in the target should be considered
                        // obsolete and should be replaced by the source.  This accounts for the scenario where shared
                        // tags are moved from one image to another. If we had merged instead of replaced, then the
                        // shared tag would not have been removed from the original image in the image info in such
                        // a scenario.
                        // See:
                        // - https://github.com/dotnet/docker-tools/pull/269
                        // - https://github.com/dotnet/docker-tools/issues/289

                        ReplaceValue(property, srcObj, targetObj);
                    }
                    else
                    {
                        MergeLists(property, srcObj, targetObj);
                    }
                }
                else
                {
                    throw new NotSupportedException($"Unsupported model property type: '{property.PropertyType.FullName}'");
                }
            }
        }

        private static void ReplaceValue(PropertyInfo property, object srcObj, object targetObj) =>
            property.SetValue(targetObj, property.GetValue(srcObj));

        private static void MergeLists(PropertyInfo property, object srcObj, object targetObj)
        {
            IList<string> srcList = (IList<string>)property.GetValue(srcObj);
            if (srcList == null)
            {
                return;
            }

            IList<string> targetList = (IList<string>)property.GetValue(targetObj);

            if (srcList.Any())
            {
                if (targetList != null)
                {
                    targetList = targetList
                        .Union(srcList)
                        .OrderBy(element => element)
                        .ToList();
                }
                else
                {
                    targetList = srcList;
                }

                property.SetValue(targetObj, targetList);
            }
        }

        private static void MergeDictionaries(PropertyInfo property, object srcObj, object targetObj,
            ImageInfoMergeOptions options)
        {
            IDictionary srcDict = (IDictionary)property.GetValue(srcObj);
            if (srcDict == null)
            {
                return;
            }

            IDictionary targetDict = (IDictionary)property.GetValue(targetObj);

            if (srcDict.Cast<object>().Any())
            {
                if (targetDict != null)
                {
                    foreach (dynamic kvp in srcDict)
                    {
                        if (targetDict.Contains(kvp.Key))
                        {
                            object newValue = kvp.Value;
                            if (newValue is string)
                            {
                                targetDict[kvp.Key] = newValue;
                            }
                            else
                            {
                                MergeData(kvp.Value, targetDict[kvp.Key], options);
                            }
                        }
                        else
                        {
                            targetDict[kvp.Key] = kvp.Value;
                        }
                    }
                }
                else
                {
                    property.SetValue(targetObj, srcDict);
                }
            }
        }
    }
}
