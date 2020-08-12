// Copyright 2020 New Relic, Inc. All rights reserved.
// SPDX-License-Identifier: Apache-2.0

#pragma once
#include <exception>
#include "../Common/xplat.h"

namespace NewRelic { namespace Profiler { namespace Configuration
{
    struct MessageException
    {
        MessageException() { }
        MessageException(xstring_t message) : _message(message) { }

        xstring_t _message;
    };

    struct ConfigurationException : MessageException
    {
        ConfigurationException() : MessageException(_X("ConfigurationException")) {}
        ConfigurationException(xstring_t message) : MessageException(message) {}
    };
}}}
