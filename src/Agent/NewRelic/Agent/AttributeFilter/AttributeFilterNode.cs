﻿using System;

namespace NewRelic.Agent
{
    internal class AttributeFilterNode
    {
        public readonly String Key;

        public readonly Boolean Wildcard;

        public readonly AttributeDestinations DestinationIncludes;
        public readonly AttributeDestinations DestinationExcludes;

        public AttributeFilterNode(String key, AttributeDestinations includes, AttributeDestinations excludes)
        {
            if (key.EndsWith("*"))
            {
                Wildcard = true;
                Key = key.Substring(0, key.Length - 1);
            }
            else
            {
                Key = key;
            }
            DestinationIncludes = includes;
            DestinationExcludes = excludes;
        }
    }
}
