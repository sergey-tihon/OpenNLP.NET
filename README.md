OpenNLP for .NET
=====================

`OpenNLP for .NET` is a port of OpenNLP to .NET.

This project contains build scripts that recompile OpenNLP `.jar` packages to .NET assemblies using [IKVM.NET](http://www.ikvm.net/) and tests that help to be sure that recompiled packages are workable. Recompiled ass are available on [NuGet](https://www.nuget.org/packages/OpenNLP.NET/).

All samples are available in tests.

Versioning
----------

Versioning model used for NuGet packages is aligned to versioning used by OpenNLP Team. 
For example, if you get `OpenNLP` package from [OpenNLP site](https://opennlp.apache.org/) with version `1.5.3`, then the NuGet version of this package has a version `1.5.3.x`, where `x` is the greatest that is available on NuGet. Last number is used for internal versioning of .NET assemblies.

Licensing
----------
Project is licensed under the terms of the [Apache 2.0 license](https://www.apache.org/licenses/LICENSE-2.0.html)
