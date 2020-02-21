# Call Center Analytics Sample
Sample code for processing recorded customer calls using Azure Cognitive Services Text Analytics APIs.

The sample is referenced by this [(blog post)](https://azure.microsoft.com/en-us/blog/using-text-analytics-in-call-centers/).

<strong>[New]</strong>
Added a no-code example using Power Automate [here](https://github.com/rlagh2/textanalytics/blob/master/no-code%20examples/speech%20and%20text%20apis/Converting%20audio%20to%20text%20-%20Part%20I.md).

## Pre-requisites

1. Azure subscription [(Free account sign-up)](https://azure.microsoft.com/en-us/free/)
2. Speech API key [(Sign up here)](https://azure.microsoft.com/en-us/services/cognitive-services/speech-to-text/)
3. Text Analytics API key [(Sign up here)](https://azure.microsoft.com/en-us/services/cognitive-services/text-analytics/)
4. Azure Storage to store your recorded calls (e.g. files in .mp3 format and preferably in stereo)

## Tools
- Visual Studio 2017
- C#
- Text Analytics API SDK

## Running the code
The sample is a console app written in C#. To get started, 
1. Clone the repo and open the .sln file using Visual Studio 2017 or 2019. 
2. Create two containers in your Azure Storage called audio and output
3. Upload mp3 files to audio container (see SampleAudio folder)
4. Update the Program.cs file with:
    1. Storage connection string
    2. Speech API key
    3. Text Analytics key
4. Install Cognitive Services Text Analytics API NuGet Package

## Overview
This sample loads call data from Azure Storage, converts the call to text, then extracts sentiment and key phrases and stores them in a CSV file for analysis.  Speech API calls are based on [this sample](https://github.com/Azure-Samples/cognitive-services-speech-sdk/tree/master/samples/batch).

After CSV files are created, you can use the PBI dashboard to view the visualizations.

## The overall workflow

![](azure-inbound.svg)

## Power BI

The PBIX file and instructions to create the visualizations are included in the PowerBI folder. Sample view:

![](PowerBI/screenshots/view3.JPG)

