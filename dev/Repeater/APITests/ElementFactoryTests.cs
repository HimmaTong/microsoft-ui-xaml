﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Common;
using MUXControlsTestApp.Utilities;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Markup;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Tests.MUXControls.ApiTests.RepeaterTests.Common;
using Windows.UI.Xaml.Tests.MUXControls.ApiTests.RepeaterTests.Common.Mocks;

#if USING_TAEF
using WEX.TestExecution;
using WEX.TestExecution.Markup;
using WEX.Logging.Interop;
#else
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.Logging;
#endif

namespace Windows.UI.Xaml.Tests.MUXControls.ApiTests.RepeaterTests
{
#if !BUILD_WINDOWS
    using ElementFactoryGetArgs = Microsoft.UI.Xaml.Controls.ElementFactoryGetArgs;
    using ElementFactoryRecycleArgs = Microsoft.UI.Xaml.Controls.ElementFactoryRecycleArgs;
    using ScrollAnchorProvider = Microsoft.UI.Xaml.Controls.ScrollAnchorProvider;
    using ItemsRepeater = Microsoft.UI.Xaml.Controls.ItemsRepeater;
    using RecyclePool = Microsoft.UI.Xaml.Controls.RecyclePool;
    using RecyclingElementFactory = Microsoft.UI.Xaml.Controls.RecyclingElementFactory;
    using RepeaterTestHooks = Microsoft.UI.Private.Controls.RepeaterTestHooks;
    using SelectTemplateEventArgs = Microsoft.UI.Xaml.Controls.SelectTemplateEventArgs;
    using StackLayout = Microsoft.UI.Xaml.Controls.StackLayout;
    using VirtualizingLayout = Microsoft.UI.Xaml.Controls.VirtualizingLayout;
#endif

    [TestClass]
    public class ElementFactoryTests : TestsBase
    {
        [TestMethod]
        public void ValidateRecycling()
        {
            if (PlatformConfiguration.IsOsVersionGreaterThan(OSVersion.Redstone4))
            {
                //BUGBUG Bug 19277320: MUX Repeater tests fail on RS5_Release
                return;
            }


            RunOnUIThread.Execute(() =>
            {
                var elementFactory = new RecyclingElementFactory()
                {
                    RecyclePool = new RecyclePool(),
                };
                elementFactory.Templates["even"] = (DataTemplate)XamlReader.Load(
                    @"<DataTemplate  xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                        <TextBlock Text='even' />
                    </DataTemplate>");
                elementFactory.Templates["odd"] = (DataTemplate)XamlReader.Load(
                    @"<DataTemplate  xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                        <TextBlock Text='odd' />
                    </DataTemplate>");

                elementFactory.SelectTemplateKey +=
                delegate (RecyclingElementFactory sender, SelectTemplateEventArgs args)
                {
                    args.TemplateKey = ((int)args.DataContext % 2 == 0) ? "even" : "odd";
                };

                const int numItems = 10;
                ItemsRepeater repeater = new ItemsRepeater()
                {
                    ItemsSource = Enumerable.Range(0, numItems),
#if BUILD_WINDOWS
                    ItemTemplate = (Windows.UI.Xaml.IElementFactory)elementFactory,
#else
                    ItemTemplate = elementFactory,
#endif
                };


                var context = (ElementFactoryGetArgs)RepeaterTestHooks.CreateRepeaterElementFactoryGetArgs();
                context.Parent = repeater;
                var clearContext = (ElementFactoryRecycleArgs)RepeaterTestHooks.CreateRepeaterElementFactoryRecycleArgs();
                clearContext.Parent = repeater;

                // Element0 is of type even, a new one should be created
                context.Data = 0;
                var element0 = elementFactory.GetElement(context);
                Verify.IsNotNull(element0);
                Verify.AreEqual("even", (element0 as TextBlock).Text);
                clearContext.Element = element0;
                elementFactory.RecycleElement(clearContext);

                // Element1 is of type odd, a new one should be created
                context.Data = 1;
                var element1 = elementFactory.GetElement(context);
                Verify.IsNotNull(element1);
                Verify.AreNotSame(element0, element1);
                Verify.AreEqual("odd", (element1 as TextBlock).Text);
                clearContext.Element = element1;
                elementFactory.RecycleElement(clearContext);

                // Element0 should be recycled for element2
                context.Data = 2;
                var element2 = elementFactory.GetElement(context);
                Verify.AreEqual("even", (element2 as TextBlock).Text);
                Verify.AreSame(element0, element2);

                // Element1 should be recycled for element3
                context.Data = 3;
                var element3 = elementFactory.GetElement(context);
                Verify.AreEqual("odd", (element3 as TextBlock).Text);
                Verify.AreSame(element1, element3);
            });
        }

        // Validate data context propagation and template selection
        [TestMethod]
        public void ValidateBindingAndTemplateSelection()
        {
            RunOnUIThread.Execute(() =>
            {
                var staticTemplate = (DataTemplate)XamlReader.Load(
                   @"<DataTemplate  xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                        <TextBlock Text='static' />
                    </DataTemplate>");
                var bindTemplate = (DataTemplate)XamlReader.Load(
                    @"<DataTemplate  xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                        <TextBlock Text='{Binding}' />
                    </DataTemplate>");
                var elementFactory = new RecyclingElementFactory()
                {
                    RecyclePool = new RecyclePool(),
                    Templates =
                    {
                        { "static", staticTemplate },
                        { "bind", bindTemplate }
                    }
                };

                elementFactory.SelectTemplateKey +=
                delegate (RecyclingElementFactory sender, SelectTemplateEventArgs args)
                {
                    args.TemplateKey = ((int)args.DataContext % 2 == 0) ? "bind" : "static";
                };

                const int numItems = 10;
                ItemsRepeater repeater = null;
                Content = CreateAndInitializeRepeater
                (
                    itemsSource: Enumerable.Range(0, numItems),
                    elementFactory: elementFactory,
                    layout: new StackLayout(),
                    repeater: ref repeater
                );

                Content.UpdateLayout();

                Verify.AreEqual(numItems, VisualTreeHelper.GetChildrenCount(repeater));
                for (int i = 0; i < numItems; i++)
                {
                    var element = (TextBlock)repeater.TryGetElement(i);
                    if (i % 2 == 0)
                    {
                        // Text is bound to the data for even indicies
                        Verify.AreEqual(i.ToString(), element.Text);
                        Verify.AreEqual(i, element.DataContext);
                    }
                    else
                    {
                        // Text explicitly set on the element only for odd indicies
                        Verify.AreEqual("static", element.Text);
                    }
                }
            });
        }

        [TestMethod]
        public void ValidateCustomRecyclingElementFactory()
        {
            RunOnUIThread.Execute(() =>
            {
                var itemTemplate = (DataTemplate)XamlReader.Load(
                        @"<DataTemplate  xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                            <TextBlock Text='uninitialized' />
                        </DataTemplate>");
                ItemsRepeater repeater = null;
                const int numItems = 10;
                Content = CreateAndInitializeRepeater
                (
                   itemsSource: Enumerable.Range(0, numItems).Select(i => string.Format("Item #{0}", i)),
                   elementFactory: new RecyclingElementFactoryDerived()
                   {
                       Templates = { { "key", itemTemplate } },
                       RecyclePool = new RecyclePool(),
                       GetElementFunc = delegate (int index, UIElement owner, UIElement elementFromBase)
                       {
                           ((TextBlock)elementFromBase).Text = index.ToString();
                           return elementFromBase;
                       },
                       ClearElementFunc = delegate (UIElement element, UIElement owner)
                       {
                           ((TextBlock)element).Text = "uninitialized";
                       },
                       SelectTemplateIdFunc = delegate (object data, UIElement owner)
                       {
                           return "key";
                       },
                   },
                   layout: new StackLayout(),
                   repeater: ref repeater
                );

                Content.UpdateLayout();
                Verify.AreEqual(numItems, VisualTreeHelper.GetChildrenCount(repeater));
                for (int i = 0; i < numItems; i++)
                {
                    var element = (TextBlock)repeater.TryGetElement(i);
                    Verify.AreEqual(i.ToString(), element.Text);
                }
            });
        }

        [TestMethod]
        public void ValidateRecyclingElementFactoryWithSingleTemplate()
        {
            RunOnUIThread.Execute(() =>
            {
                var itemTemplate = (DataTemplate)XamlReader.Load(
                        @"<DataTemplate  xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                            <TextBlock Text='uninitialized' />
                        </DataTemplate>");
                ItemsRepeater repeater = null;
                const int numItems = 10;
                var elementFactory = new RecyclingElementFactoryDerived()
                {
                    Templates = { { "key", itemTemplate } },
                    RecyclePool = new RecyclePool(),
                    GetElementFunc = delegate (int index, UIElement owner, UIElement elementFromBase)
                    {
                        ((TextBlock)elementFromBase).Text = index.ToString();
                        return elementFromBase;
                    },
                    ClearElementFunc = delegate (UIElement element, UIElement owner)
                    {
                        ((TextBlock)element).Text = "uninitialized";
                    },
                };

                elementFactory.SelectTemplateKey += delegate (RecyclingElementFactory sender, SelectTemplateEventArgs args)
                {
                    Verify.Fail("SelectTemplateKey event should not be raised when using a single template");
                };

                Content = CreateAndInitializeRepeater
                (
                   itemsSource: Enumerable.Range(0, numItems).Select(i => string.Format("Item #{0}", i)),
                   elementFactory: elementFactory,
                   layout: new StackLayout(),
                   repeater: ref repeater
                );

                Content.UpdateLayout();
                Verify.AreEqual(numItems, VisualTreeHelper.GetChildrenCount(repeater));
                for (int i = 0; i < numItems; i++)
                {
                    var element = (TextBlock)repeater.TryGetElement(i);
                    Verify.AreEqual(i.ToString(), element.Text);
                }
            });
        }

        [TestMethod]
#if BUILD_WINDOWS
        [TestProperty("Ignore", "True")] // TODO 19581880: Re-enable after investigating and fixing the test failures.
#endif
        public void ValidateDataTemplateAsItemTemplate()
        {
            RunOnUIThread.Execute(() =>
            {
                var dataTemplate = (DataTemplate)XamlReader.Load(
                        @"<DataTemplate  xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                            <TextBlock Text='{Binding}' />
                        </DataTemplate>");
                ItemsRepeater repeater = null;
                const int numItems = 10;

                Content = CreateAndInitializeRepeater
                (
                   itemsSource: Enumerable.Range(0, numItems).Select(i => string.Format("{0}", i)),
                   elementFactory: dataTemplate,
                   layout: new StackLayout(),
                   repeater: ref repeater
                );

                Content.UpdateLayout();
                Verify.AreEqual(numItems, VisualTreeHelper.GetChildrenCount(repeater));
                for (int i = 0; i < numItems; i++)
                {
                    var element = (TextBlock)repeater.TryGetElement(i);
                    Verify.AreEqual(i.ToString(), element.Text);
                }

                repeater.ItemsSource = null;
                Content.UpdateLayout();

                // All the created items should be in the recycle pool now.
                var pool = RecyclePool.GetPoolInstance(dataTemplate);
                var recycledElements = GetAllElementsFromPool(pool);
                Verify.AreEqual(10, recycledElements.Count);
            });
        }

        [TestMethod]
#if BUILD_WINDOWS
        [TestProperty("Ignore", "True")] // TODO 19581880: Re-enable after investigating and fixing the test failures.
#endif
        public void ValidateDataTemplateSelectorAsItemTemplate()
        {
            RunOnUIThread.Execute(() =>
            {
                var dataTemplateOdd = (DataTemplate)XamlReader.Load(
                        @"<DataTemplate  xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                            <TextBlock Text='{Binding}' Height='30' />
                        </DataTemplate>");
                var dataTemplateEven = (DataTemplate)XamlReader.Load(
                        @"<DataTemplate  xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                            <TextBlock Text='{Binding}' Height='40' />
                        </DataTemplate>");
                ItemsRepeater repeater = null;
                const int numItems = 10;
                var selector = new MySelector()
                {
                    TemplateOdd = dataTemplateOdd,
                    TemplateEven = dataTemplateEven
                };

                Content = CreateAndInitializeRepeater
                (
                   itemsSource: Enumerable.Range(0, numItems),
                   elementFactory: selector,
                   layout: new StackLayout(),
                   repeater: ref repeater
                );

                Content.UpdateLayout();
                Verify.AreEqual(numItems, VisualTreeHelper.GetChildrenCount(repeater));
                for (int i = 0; i < numItems; i++)
                {
                    var element = (TextBlock)repeater.TryGetElement(i);
                    Verify.AreEqual(i.ToString(), element.Text);
                    Verify.AreEqual(i % 2 == 0 ? 40 : 30, element.Height);
                }

                repeater.ItemsSource = null;
                Content.UpdateLayout();

                // All the created items should be in the recycle pool now.
                var oddPool = RecyclePool.GetPoolInstance(dataTemplateOdd);
                var oddElements = GetAllElementsFromPool(oddPool);
                Verify.AreEqual(5, oddElements.Count);
                foreach(var element in oddElements)
                {
                    Verify.AreEqual(30, ((TextBlock)element).Height);
                }

                var evenPool = RecyclePool.GetPoolInstance(dataTemplateEven);
                var evenElements = GetAllElementsFromPool(evenPool);
                Verify.AreEqual(5, evenElements.Count);
                foreach (var element in evenElements)
                {
                    Verify.AreEqual(40, ((TextBlock)element).Height);
                }
            });
        }

        [TestMethod]
        public void ValidateReyclingElementFactoryWithNoTemplate()
        {
            RunOnUIThread.Execute(() =>
            {
                var elementFactory = new RecyclingElementFactoryDerived()
                {
                    RecyclePool = new RecyclePool()
                };

                Verify.Throws<COMException>(delegate
                {
                    var context = (ElementFactoryGetArgs)RepeaterTestHooks.CreateRepeaterElementFactoryGetArgs();
                    context.Parent = null;
                    context.Data = 0;

                    elementFactory.GetElement(context);
                });
            });
        }

        // Validate ability to create and use a view generator from scratch
        [TestMethod]
        public void ValidateCustomElementFactory()
        {
            RunOnUIThread.Execute(() =>
            {
                ItemsRepeater repeater = null;
                const int numItems = 10;
                Content = CreateAndInitializeRepeater
                (
                   itemsSource: Enumerable.Range(0, numItems),
                   elementFactory: new MockElementFactory()
                   {
                       GetElementFunc = delegate (int index, UIElement owner)
                       {
                           return new Button() { Content = index.ToString() };
                       },
                       ClearElementFunc = delegate (UIElement element, UIElement owner)
                       {
                           Verify.AreEqual(typeof(Button), element.GetType());
                       }
                   },
                   layout: new StackLayout(),
                   repeater: ref repeater
                );

                Content.UpdateLayout();
                for (int i = 0; i < numItems; i++)
                {
                    var element = (Button)repeater.TryGetElement(i);
                    Verify.AreEqual(i.ToString(), element.Content.ToString());
                }
            });
        }

        private ScrollAnchorProvider CreateAndInitializeRepeater(
            object itemsSource,
            VirtualizingLayout layout,
            object elementFactory,
            ref ItemsRepeater repeater)
        {
            repeater = new ItemsRepeater()
            {
                ItemsSource = itemsSource,
                Layout = layout,
#if BUILD_WINDOWS
                ItemTemplate = (Windows.UI.Xaml.IElementFactory)elementFactory,
#else
                ItemTemplate = elementFactory,
#endif
            };

            return new ScrollAnchorProvider()
            {
                Width = 400,
                Height = 400,
                Content = new ScrollViewer()
                {
                    Content = repeater
                }
            };
        }

        private List<UIElement> GetAllElementsFromPool(RecyclePool pool, string key="")
        {
            List<UIElement> elements = new List<UIElement>();
            bool poolEmpty = false;
            while(!poolEmpty)
            {
                var next = pool.TryGetElement(key);
                if(next != null)
                {
                    elements.Add(next);
                }

                poolEmpty = next == null;
            }

            return elements;
        }
    }

    public class MySelector : DataTemplateSelector
    {
        public DataTemplate TemplateOdd { get; set; }

        public DataTemplate TemplateEven { get; set; }

        protected override DataTemplate SelectTemplateCore(object item)
        {
            return (((int)item) % 2 == 0) ? TemplateEven : TemplateOdd;
        }
    }
}
