// ----------------------------------------------------------------------------------
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MS.Test.Common.MsTestLib
{
    public interface ILogger
    {
        void WriteError(
            string msg,
            params object[] objToLog);

        void WriteWarning(
            string msg,
            params object[] objToLog);

        void WriteInfo(
            string msg,
            params object[] objToLog);

        void WriteVerbose(
            string msg,
            params object[] objToLog);

        void StartTest(
            string testId);

        void EndTest(
            string testId,
            TestResult result);

        object GetLogger();

        void Close();

    }

    public enum TestResult
    {
        PASS,
        FAIL
    }


}