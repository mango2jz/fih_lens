using System.Diagnostics;
using System.Runtime.InteropServices;
using FihLens.Application.Capture;

namespace FihLens.Windows.Capture;

/// <summary>Private UIA ABI declarations copied from the installed SDK. Returned COM objects stay on the MTA worker.</summary>
internal static class UiAutomationInterop
{
    private static readonly Guid AutomationClsid = new("e22ad333-b25f-460c-83d0-0581107395c9");
    private static readonly Guid AutomationInterfaceId = new("34723aff-0c9d-49d0-9896-7ab52df8cd8a");
    private const int UiaIsPasswordPropertyId = 30019;
    private const int UiaTextPatternId = 10014;
    private const int UiaElementNotAvailable = unchecked((int)0x80040201);
    private const int UiaNotSupported = unchecked((int)0x80040204);
    private const int AccessDenied = unchecked((int)0x80070005);

    internal static SelectedTextCaptureResult Capture(SelectedTextSource source, long deadline, CancellationToken cancellationToken,
        Func<bool> sourceIsCurrent)
    {
        IUIAutomation2? automation = null;
        IUIAutomationElement? root = null;
        IUIAutomationElement? focused = null;
        IUIAutomationElement? firstFocused = null;
        bool secondFocusMapped = false;
        IUIAutomationTreeWalker? walker = null;
        IUIAutomationTextPattern? pattern = null;
        IUIAutomationTextRangeArray? firstRanges = null;
        IUIAutomationTextRangeArray? secondRanges = null;
        IUIAutomationTextRange? selected = null;
        IUIAutomationTextRange? clone = null;
        try
        {
            EnsureLive(source, deadline, cancellationToken, sourceIsCurrent);
            int hr = CoCreateInstance(in AutomationClsid, 0, 1, in AutomationInterfaceId, out object instance);
            if (hr < 0) return Map(hr);
            automation = (IUIAutomation2)instance;
            EnsureLive(source, deadline, cancellationToken, sourceIsCurrent);
            if (automation.SetConnectionTimeout(500) < 0) return SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.Unavailable);
            EnsureLive(source, deadline, cancellationToken, sourceIsCurrent);
            if (automation.SetTransactionTimeout(750) < 0)
                return SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.Unavailable);

            EnsureLive(source, deadline, cancellationToken, sourceIsCurrent);
            hr = automation.ElementFromHandle(source.SourceWindow, out root);
            if (hr < 0 || root is null) return Map(hr < 0 ? hr : UiaElementNotAvailable);
            EnsureLive(source, deadline, cancellationToken, sourceIsCurrent);
            hr = automation.GetFocusedElement(out focused);
            if (hr < 0 || focused is null) return Map(hr < 0 ? hr : UiaElementNotAvailable);
            firstFocused = focused;
            EnsureLive(source, deadline, cancellationToken, sourceIsCurrent);
            hr = automation.RawViewWalker(out walker);
            if (hr < 0 || walker is null) return Map(hr < 0 ? hr : UiaElementNotAvailable);
            var membership = IsWithinRoot(automation, walker, root, focused, deadline, cancellationToken, sourceIsCurrent);
            if (membership != SelectedTextCaptureOutcome.Success) return SelectedTextCaptureResult.Failure(membership);

            EnsureLive(source, deadline, cancellationToken, sourceIsCurrent);
            // The COM marshaller VariantClears this out value; manually clearing it would double-release interface values.
            if (focused.GetCurrentPropertyValueEx(UiaIsPasswordPropertyId, true, out object? passwordValue) < 0 || passwordValue is not bool password || password)
            {
                ReleaseVariantObject(passwordValue);
                return SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.Sensitive);
            }

            try
            {
                EnsureLive(source, deadline, cancellationToken, sourceIsCurrent);
                hr = focused.GetCurrentPatternAs(UiaTextPatternId, typeof(IUIAutomationTextPattern).GUID, out pattern);
            }
            finally { ReleaseVariantObject(passwordValue); }
            if (hr is UiaNotSupported or unchecked((int)0x80004002)) return SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.Unsupported);
            if (hr < 0 || pattern is null) return Map(hr < 0 ? hr : UiaNotSupported);

            EnsureLive(source, deadline, cancellationToken, sourceIsCurrent);
            hr = pattern.GetSelection(out firstRanges);
            if (hr < 0) return Map(hr);
            if (firstRanges is null) return SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.NoText);
            EnsureLive(source, deadline, cancellationToken, sourceIsCurrent);
            if ((hr = firstRanges.get_Length(out int count)) < 0) return Map(hr);
            if (count == 0) return SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.NoText);
            if (count != 1) return SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.MultipleRanges);
            EnsureLive(source, deadline, cancellationToken, sourceIsCurrent);
            if ((hr = firstRanges.GetElement(0, out selected)) < 0 || selected is null) return Map(hr < 0 ? hr : UiaElementNotAvailable);
            EnsureLive(source, deadline, cancellationToken, sourceIsCurrent);
            if ((hr = selected.CompareEndpoints(0, selected, 1, out int extent)) < 0) return Map(hr);
            if (extent >= 0) return SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.NoText);
            EnsureLive(source, deadline, cancellationToken, sourceIsCurrent);
            if ((hr = selected.Clone(out clone)) < 0 || clone is null) return Map(hr < 0 ? hr : UiaElementNotAvailable);

            EnsureLive(source, deadline, cancellationToken, sourceIsCurrent);
            if ((hr = selected.GetText(2001, out string firstText)) < 0) return Map(hr);
            if (firstText.Length > SelectedTextCaptureResult.MaxTextLength) return SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.TooLarge);

            EnsureLive(source, deadline, cancellationToken, sourceIsCurrent);
            hr = automation.GetFocusedElement(out focused);
            secondFocusMapped = focused is not null;
            if (hr < 0 || focused is null) return Map(hr < 0 ? hr : UiaElementNotAvailable);
            membership = IsWithinRoot(automation, walker, root, focused, deadline, cancellationToken, sourceIsCurrent);
            if (membership != SelectedTextCaptureOutcome.Success) return SelectedTextCaptureResult.Failure(membership);
            EnsureLive(source, deadline, cancellationToken, sourceIsCurrent);
            if ((hr = automation.CompareElements(firstFocused, focused, out bool sameFocus)) < 0) return Map(hr);
            if (!sameFocus) return SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.SourceChanged);
            EnsureLive(source, deadline, cancellationToken, sourceIsCurrent);
            if ((hr = focused.GetCurrentPropertyValueEx(UiaIsPasswordPropertyId, true, out passwordValue)) < 0 || passwordValue is not bool isPassword || isPassword)
            {
                ReleaseVariantObject(passwordValue);
                return SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.Sensitive);
            }
            ReleaseVariantObject(passwordValue);
            EnsureLive(source, deadline, cancellationToken, sourceIsCurrent);
            if ((hr = pattern.GetSelection(out secondRanges)) < 0) return Map(hr);
            if (secondRanges is null) return SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.NoText);
            EnsureLive(source, deadline, cancellationToken, sourceIsCurrent);
            if ((hr = secondRanges.get_Length(out count)) < 0) return Map(hr);
            if (count < 0) return SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.Failed);
            if (count != 1) return SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.SourceChanged);
            IUIAutomationTextRange? second = null;
            try
            {
                EnsureLive(source, deadline, cancellationToken, sourceIsCurrent);
                if ((hr = secondRanges.GetElement(0, out second)) < 0 || second is null) return Map(hr < 0 ? hr : UiaElementNotAvailable);
                EnsureLive(source, deadline, cancellationToken, sourceIsCurrent);
                if ((hr = second.CompareEndpoints(0, clone, 0, out int startDelta)) < 0) return Map(hr);
                if (startDelta != 0) return SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.SourceChanged);
                EnsureLive(source, deadline, cancellationToken, sourceIsCurrent);
                if ((hr = second.CompareEndpoints(1, clone, 1, out int endDelta)) < 0) return Map(hr);
                if (endDelta != 0) return SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.SourceChanged);
                EnsureLive(source, deadline, cancellationToken, sourceIsCurrent);
                if ((hr = second.GetText(2001, out string secondText)) < 0) return Map(hr);
                if (secondText.Length > SelectedTextCaptureResult.MaxTextLength) return SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.TooLarge);
                EnsureLive(source, deadline, cancellationToken, sourceIsCurrent);
                if (!string.Equals(firstText, secondText, StringComparison.Ordinal))
                    return SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.SourceChanged);
                return string.IsNullOrWhiteSpace(secondText)
                    ? SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.NoText)
                    : SelectedTextCaptureResult.Success(secondText);
            }
            finally { Release(ref second); }
        }
        catch (OperationCanceledException) { return SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.Timeout); }
        catch (CaptureDeadlineException) { return SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.Timeout); }
        catch (SourceChangedException) { return SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.SourceChanged); }
        catch (Exception exception) { return Map(exception.HResult); }
        finally
        {
            Release(ref secondRanges); Release(ref firstRanges); Release(ref clone); Release(ref selected);
            Release(ref pattern); Release(ref walker);
            if (ReferenceEquals(focused, firstFocused) && secondFocusMapped)
            {
                Release(ref focused);
                var duplicateMapping = firstFocused;
                Release(ref duplicateMapping);
                firstFocused = null;
            }
            else if (ReferenceEquals(focused, firstFocused))
            {
                Release(ref focused);
                firstFocused = null;
            }
            else
            {
                Release(ref focused);
                Release(ref firstFocused);
            }
            Release(ref root); Release(ref automation);
        }
    }

    private static SelectedTextCaptureOutcome IsWithinRoot(IUIAutomation2 automation, IUIAutomationTreeWalker walker,
        IUIAutomationElement root, IUIAutomationElement focused, long deadline, CancellationToken token, Func<bool> sourceCurrent)
    {
        IUIAutomationElement? current = focused;
        bool ownsCurrent = false;
        try
        {
            for (int depth = 0; depth <= 32; depth++)
            {
                EnsureLiveForOutcome(deadline, token, sourceCurrent);
                int hr = automation.CompareElements(root, current, out bool same);
                if (hr < 0) return OutcomeFor(hr);
                if (same) return SelectedTextCaptureOutcome.Success;
                if (depth == 32) return SelectedTextCaptureOutcome.Unavailable;
                EnsureLiveForOutcome(deadline, token, sourceCurrent);
                hr = walker.GetParentElement(current, out IUIAutomationElement? parent);
            if (hr < 0) return OutcomeFor(hr);
            if (parent is null) return SelectedTextCaptureOutcome.Unavailable;
                if (ownsCurrent) Release(ref current);
                current = parent;
                ownsCurrent = true;
            }
            return SelectedTextCaptureOutcome.Unavailable;
        }
        catch (CaptureDeadlineException) { return SelectedTextCaptureOutcome.Timeout; }
        catch (SourceChangedException) { return SelectedTextCaptureOutcome.SourceChanged; }
        catch (OperationCanceledException) { return SelectedTextCaptureOutcome.Timeout; }
        finally { if (ownsCurrent) Release(ref current); }
    }

    private static void EnsureLive(SelectedTextSource source, long deadline, CancellationToken token, Func<bool> sourceCurrent)
    {
        token.ThrowIfCancellationRequested();
        if (Stopwatch.GetTimestamp() >= deadline) throw new CaptureDeadlineException();
        if (!sourceCurrent()) throw new SourceChangedException();
    }
    private static void EnsureLiveForOutcome(long deadline, CancellationToken token, Func<bool> sourceCurrent)
    {
        token.ThrowIfCancellationRequested();
        if (Stopwatch.GetTimestamp() >= deadline) throw new CaptureDeadlineException();
        if (!sourceCurrent()) throw new SourceChangedException();
    }
    private static SelectedTextCaptureOutcome OutcomeFor(int hr) => hr == AccessDenied ? SelectedTextCaptureOutcome.Denied :
        hr == UiaElementNotAvailable ? SelectedTextCaptureOutcome.Unavailable : SelectedTextCaptureOutcome.Failed;
    private static SelectedTextCaptureResult Map(int hr) => SelectedTextCaptureResult.Failure(hr switch
    {
        AccessDenied => SelectedTextCaptureOutcome.Denied,
        UiaElementNotAvailable => SelectedTextCaptureOutcome.Unavailable,
        UiaNotSupported => SelectedTextCaptureOutcome.Unsupported,
        unchecked((int)0x8001011F) or unchecked((int)0x80131505) => SelectedTextCaptureOutcome.Timeout,
        _ => SelectedTextCaptureOutcome.Failed
    });
    private static void ReleaseVariantObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }
    private static void Release<T>(ref T? value) where T : class
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        value = null;
    }

    private sealed class CaptureDeadlineException : Exception { }
    private sealed class SourceChangedException : Exception { }

    [DllImport("ole32.dll", PreserveSig = true)] private static extern int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out object instance);

    [ComImport, Guid("34723aff-0c9d-49d0-9896-7ab52df8cd8a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation2
    {
        [PreserveSig] int CompareElements(IUIAutomationElement first, IUIAutomationElement second, [MarshalAs(UnmanagedType.Bool)] out bool same);
        [PreserveSig] int CompareRuntimeIds(nint first, nint second, [MarshalAs(UnmanagedType.Bool)] out bool same);
        [PreserveSig] int GetRootElement(out nint element);
        [PreserveSig] int ElementFromHandle(nint hwnd, out IUIAutomationElement element);
        [PreserveSig] int ElementFromPoint(nint point, out nint element);
        [PreserveSig] int GetFocusedElement(out IUIAutomationElement element);
        [PreserveSig] int GetRootElementBuildCache(nint cache, out nint element);
        [PreserveSig] int ElementFromHandleBuildCache(nint hwnd, nint cache, out nint element);
        [PreserveSig] int ElementFromPointBuildCache(nint point, nint cache, out nint element);
        [PreserveSig] int GetFocusedElementBuildCache(nint cache, out nint element);
        [PreserveSig] int CreateTreeWalker(nint condition, out nint walker);
        [PreserveSig] int get_ControlViewWalker(out nint walker);
        [PreserveSig] int get_ContentViewWalker(out nint walker);
        [PreserveSig] int RawViewWalker(out IUIAutomationTreeWalker walker);
        [PreserveSig] int get_RawViewCondition(out nint condition);
        [PreserveSig] int get_ControlViewCondition(out nint condition);
        [PreserveSig] int get_ContentViewCondition(out nint condition);
        [PreserveSig] int CreateCacheRequest(out nint cache);
        [PreserveSig] int CreateTrueCondition(out nint condition);
        [PreserveSig] int CreateFalseCondition(out nint condition);
        [PreserveSig] int CreatePropertyCondition(int property, nint value, out nint condition);
        [PreserveSig] int CreatePropertyConditionEx(int property, nint value, int flags, out nint condition);
        [PreserveSig] int CreateAndCondition(nint a, nint b, out nint condition);
        [PreserveSig] int CreateAndConditionFromArray(nint conditions, out nint condition);
        [PreserveSig] int CreateAndConditionFromNativeArray(nint conditions, int count, out nint condition);
        [PreserveSig] int CreateOrCondition(nint a, nint b, out nint condition);
        [PreserveSig] int CreateOrConditionFromArray(nint conditions, out nint condition);
        [PreserveSig] int CreateOrConditionFromNativeArray(nint conditions, int count, out nint condition);
        [PreserveSig] int CreateNotCondition(nint condition, out nint result);
        [PreserveSig] int AddAutomationEventHandler(int eventId, nint element, int scope, nint cache, nint handler);
        [PreserveSig] int RemoveAutomationEventHandler(int eventId, nint element, nint handler);
        [PreserveSig] int AddPropertyChangedEventHandlerNativeArray(nint element, int scope, nint cache, nint handler, nint properties, int count);
        [PreserveSig] int AddPropertyChangedEventHandler(nint element, int scope, nint cache, nint handler, nint properties);
        [PreserveSig] int RemovePropertyChangedEventHandler(nint element, nint handler);
        [PreserveSig] int AddStructureChangedEventHandler(nint element, int scope, nint cache, nint handler);
        [PreserveSig] int RemoveStructureChangedEventHandler(nint element, nint handler);
        [PreserveSig] int AddFocusChangedEventHandler(nint cache, nint handler);
        [PreserveSig] int RemoveFocusChangedEventHandler(nint handler);
        [PreserveSig] int RemoveAllEventHandlers();
        [PreserveSig] int IntNativeArrayToSafeArray(nint array, int count, out nint result);
        [PreserveSig] int IntSafeArrayToNativeArray(nint array, out nint result, out int count);
        [PreserveSig] int RectToVariant(nint rect, nint result);
        [PreserveSig] int VariantToRect(nint variant, nint rect);
        [PreserveSig] int SafeArrayToRectNativeArray(nint array, out nint result, out int count);
        [PreserveSig] int CreateProxyFactoryEntry(nint factory, out nint result);
        [PreserveSig] int get_ProxyFactoryMapping(out nint mapping);
        [PreserveSig] int GetPropertyProgrammaticName(int property, out nint name);
        [PreserveSig] int GetPatternProgrammaticName(int pattern, out nint name);
        [PreserveSig] int PollForPotentialSupportedPatterns(nint element, out nint patterns, out nint names);
        [PreserveSig] int PollForPotentialSupportedProperties(nint element, out nint properties, out nint names);
        [PreserveSig] int CheckNotSupported(nint value, [MarshalAs(UnmanagedType.Bool)] out bool unsupported);
        [PreserveSig] int get_ReservedNotSupportedValue([MarshalAs(UnmanagedType.Struct)] out object value);
        [PreserveSig] int get_ReservedMixedAttributeValue([MarshalAs(UnmanagedType.Struct)] out object value);
        [PreserveSig] int ElementFromIAccessible(nint accessible, int childId, out nint element);
        [PreserveSig] int ElementFromIAccessibleBuildCache(nint accessible, int childId, nint cache, out nint element);
        [PreserveSig] int get_AutoSetFocus([MarshalAs(UnmanagedType.Bool)] out bool value);
        [PreserveSig] int put_AutoSetFocus([MarshalAs(UnmanagedType.Bool)] bool value);
        [PreserveSig] int GetConnectionTimeout(out uint value);
        [PreserveSig] int SetConnectionTimeout(uint value);
        [PreserveSig] int GetTransactionTimeout(out uint value);
        [PreserveSig] int SetTransactionTimeout(uint value);
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        [PreserveSig] int SetFocus(); [PreserveSig] int GetRuntimeId(out nint value);
        [PreserveSig] int FindFirst(int scope, nint condition, out nint value); [PreserveSig] int FindAll(int scope, nint condition, out nint value);
        [PreserveSig] int FindFirstBuildCache(int scope, nint condition, nint cache, out nint value); [PreserveSig] int FindAllBuildCache(int scope, nint condition, nint cache, out nint value);
        [PreserveSig] int BuildUpdatedCache(nint cache, out nint value); [PreserveSig] int GetCurrentPropertyValue(int property, nint value);
        [PreserveSig] int GetCurrentPropertyValueEx(int property, [MarshalAs(UnmanagedType.Bool)] bool ignoreDefault, [MarshalAs(UnmanagedType.Struct)] out object value);
        [PreserveSig] int GetCachedPropertyValue(int property, nint value); [PreserveSig] int GetCachedPropertyValueEx(int property, [MarshalAs(UnmanagedType.Bool)] bool ignoreDefault, nint value);
        [PreserveSig] int GetCurrentPatternAs(int pattern, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationTextPattern value);
    }

    [ComImport, Guid("4042c624-389c-4afc-a630-9df854a541fc"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTreeWalker
    {
        [PreserveSig] int GetParentElement(IUIAutomationElement element, out IUIAutomationElement? parent);
    }
    [ComImport, Guid("32eba289-3583-42c9-9c59-3b6d9a1e9b6a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextPattern
    {
        [PreserveSig] int RangeFromPoint(nint point, out nint range); [PreserveSig] int RangeFromChild(IUIAutomationElement child, out nint range);
        [PreserveSig] int GetSelection(out IUIAutomationTextRangeArray ranges);
    }
    [ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextRangeArray
    {
        [PreserveSig] int get_Length(out int length);
        [PreserveSig] int GetElement(int index, out IUIAutomationTextRange range);
    }
    [ComImport, Guid("a543cc6a-f4ae-494b-8239-c814481187a8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextRange
    {
        [PreserveSig] int Clone(out IUIAutomationTextRange range); [PreserveSig] int Compare(IUIAutomationTextRange range, [MarshalAs(UnmanagedType.Bool)] out bool same);
        [PreserveSig] int CompareEndpoints(int sourceEndpoint, IUIAutomationTextRange range, int targetEndpoint, out int result);
        [PreserveSig] int ExpandToEnclosingUnit(int unit); [PreserveSig] int FindAttribute(int id, nint value, [MarshalAs(UnmanagedType.Bool)] bool backward, out nint range);
        [PreserveSig] int FindText(nint text, [MarshalAs(UnmanagedType.Bool)] bool backward, [MarshalAs(UnmanagedType.Bool)] bool ignoreCase, out nint range);
        [PreserveSig] int GetAttributeValue(int id, nint value); [PreserveSig] int GetBoundingRectangles(out nint values);
        [PreserveSig] int GetEnclosingElement(out nint value); [PreserveSig] int GetText(int maxLength, [MarshalAs(UnmanagedType.BStr)] out string text);
    }
}
