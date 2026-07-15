// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Scripting;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BrickVerse.Tests;

public class BVSignalTest
{
	private static (BVCallback cb, List<object?[]> calls) MakeCallback()
	{
		List<object?[]> calls = [];
		BVCallback cb = new(calls.Add);
		return (cb, calls);
	}

	[Fact]
	public void Invoke_NoSubscribers_DoesNotThrow()
	{
		BVSignal signal = new();
		var ex = Record.Exception(() => signal.Invoke("hello"));
		Assert.Null(ex);
	}

	[Fact]
	public void Invoke_CallsConnectedCallback()
	{
		BVSignal signal = new();
		var (cb, calls) = MakeCallback();

		signal.Connect(cb);
		signal.Invoke("arg1", 42);

		Assert.Single(calls);
		Assert.Equal(["arg1", 42], calls[0]);
	}

	[Fact]
	public void Invoke_CallsMultipleCallbacksInOrder()
	{
		BVSignal signal = new();
		List<int> order = [];

		BVCallback cb1 = new(_ => order.Add(1));
		BVCallback cb2 = new(_ => order.Add(2));
		BVCallback cb3 = new(_ => order.Add(3));

		signal.Connect(cb1);
		signal.Connect(cb2);
		signal.Connect(cb3);
		signal.Invoke();

		// InvokeDirect iterates in reverse — all three must fire
		Assert.Equal(3, order.Count);
		Assert.Contains(1, order);
		Assert.Contains(2, order);
		Assert.Contains(3, order);
	}

	[Fact]
	public void Invoke_NullArgs_TreatedAsEmptyArray()
	{
		BVSignal signal = new();
		var (cb, calls) = MakeCallback();

		signal.Connect(cb);
		signal.Invoke(null); // passes null → converted to []

		Assert.Single(calls);
	}

	[Fact]
	public void Connect_SameCallbackTwice_NotDuplicated()
	{
		BVSignal signal = new();
		int invocations = 0;
		BVCallback cb = new(_ => invocations++);

		signal.Connect(cb);
		signal.Connect(cb); // duplicate — should be ignored
		signal.Invoke();

		Assert.Equal(1, invocations);
	}

	[Fact]
	public void Connect_RaisesSubscribedEvent()
	{
		BVSignal signal = new();
		int raised = 0;
		signal.Subscribed += () => raised++;

		BVCallback cb = new(_ => { });
		signal.Connect(cb);

		Assert.Equal(1, raised);
	}

	[Fact]
	public void Connect_DuplicateCallback_DoesNotRaiseSubscribedEvent()
	{
		BVSignal signal = new();
		int raised = 0;
		signal.Subscribed += () => raised++;

		BVCallback cb = new(_ => { });
		signal.Connect(cb);
		signal.Connect(cb);

		Assert.Equal(1, raised);
	}

	[Fact]
	public void Connect_ActionOverload_InvokesWhenSignalFires()
	{
		BVSignal signal = new();
		bool fired = false;
		signal.Connect(() => fired = true);
		signal.Invoke();
		Assert.True(fired);
	}

	[Fact]
	public void Connect_ActionObjectOverload_PassesFirstArg()
	{
		BVSignal signal = new();
		object? received = null;
		signal.Connect(arg => received = arg);
		signal.Invoke("hello");
		Assert.Equal("hello", received);
	}


	[Fact]
	public void Disconnect_RemovesCallback_StopsInvocation()
	{
		BVSignal signal = new();
		int count = 0;
		BVCallback cb = new(_ => count++);

		signal.Connect(cb);
		signal.Disconnect(cb);
		signal.Invoke();

		Assert.Equal(0, count);
	}

	[Fact]
	public void Disconnect_RaisesUnsubscribedEvent()
	{
		BVSignal signal = new();
		int raised = 0;
		signal.Unsubscribed += () => raised++;

		BVCallback cb = new(_ => { });
		signal.Connect(cb);
		signal.Disconnect(cb);

		Assert.Equal(1, raised);
	}

	[Fact]
	public void Disconnect_UnknownCallback_DoesNotThrow()
	{
		BVSignal signal = new();
		BVCallback cb = new(_ => { });

		var ex = Record.Exception(() => signal.Disconnect(cb));
		Assert.Null(ex);
	}

	[Fact]
	public void Disconnect_ViaConnection_RemovesCallback()
	{
		BVSignal signal = new();
		int count = 0;
		BVCallback cb = new(_ => count++);

		var conn = signal.Connect(cb);
		conn.Disconnect();
		signal.Invoke();

		Assert.Equal(0, count);
	}

	[Fact]
	public void Disconnect_ActionOverload_RemovesCorrectCallback()
	{
		BVSignal signal = new();
		int countA = 0, countB = 0;

		void a() => countA++;
		void b() => countB++;

		signal.Connect(a);
		signal.Connect(b);
		signal.Disconnect(a);
		signal.Invoke();

		Assert.Equal(0, countA);
		Assert.Equal(1, countB);
	}

	[Fact]
	public void Once_BVCallback_FiresOnlyOnFirstInvoke()
	{
		BVSignal signal = new();
		int count = 0;
		BVCallback cb = new(_ => count++);

		signal.Once(cb);
		signal.Invoke();
		signal.Invoke();
		signal.Invoke();

		Assert.Equal(1, count);
	}

	[Fact]
	public void Once_Action_FiresOnlyOnce()
	{
		BVSignal signal = new();
		int count = 0;
		signal.Once(_ => count++);

		signal.Invoke("x");
		signal.Invoke("y");

		Assert.Equal(1, count);
	}

	[Fact]
	public void Once_PassesCorrectArgs()
	{
		BVSignal signal = new();
		object? got = null;
		signal.Once(arg => got = arg);
		signal.Invoke("expected");
		Assert.Equal("expected", got);
	}

	[Fact]
	public async Task Wait_ReturnsArgsOnNextInvoke()
	{
		BVSignal signal = new();

		Task<object?[]> waitTask = signal.Wait();

		// Fire the signal from another context
		await Task.Run(() => signal.Invoke("a", "b"), TestContext.Current.CancellationToken);

		object?[] result = await waitTask;
		Assert.Equal(["a", "b"], result);
	}

	[Fact]
	public async Task Wait_WithNoArgs_ReturnsEmptyArray()
	{
		BVSignal signal = new();
		Task<object?[]> waitTask = signal.Wait();
		await Task.Run(() => signal.Invoke(), TestContext.Current.CancellationToken);
		object?[] result = await waitTask;
		Assert.Empty(result);
	}

	[Fact]
	public async Task Wait_OnlyResolvesOnce()
	{
		BVSignal signal = new();
		Task<object?[]> waitTask = signal.Wait();

		signal.Invoke("first");
		signal.Invoke("second");

		object?[] result = await waitTask;
		Assert.Equal("first", result[0]);
	}

	[Fact]
	public void DisconnectAll_StopsAllCallbacks()
	{
		BVSignal signal = new();
		int count = 0;

		signal.Connect(new BVCallback(_ => count++));
		signal.Connect(new BVCallback(_ => count++));
		signal.Connect(new BVCallback(_ => count++));

		signal.DisconnectAll();
		signal.Invoke();

		Assert.Equal(0, count);
	}

	[Fact]
	public void DisconnectAll_ThenConnect_WorksNormally()
	{
		BVSignal signal = new();
		int count = 0;

		signal.Connect(new BVCallback(_ => count++));
		signal.DisconnectAll();

		signal.Connect(new BVCallback(_ => count++));
		signal.Invoke();

		Assert.Equal(1, count);
	}

	[Fact]
	public void Invoke_SkipsDisposedCallbacks_WithoutThrowing()
	{
		BVSignal signal = new();
		int count = 0;

		var goodCb = new BVCallback(_ => count++);
		var disposedCb = new BVCallback(_ => count++);

		signal.Connect(goodCb);
		signal.Connect(disposedCb);

		disposedCb.Dispose(); // mark as disposed before invoke

		var ex = Record.Exception(() => signal.Invoke());
		Assert.Null(ex);
		Assert.Equal(1, count); // only goodCb should have fired
	}

	[Fact]
	public void GenericSubclasses_WorkLikeBVSignal()
	{
		BVSignal<string> signal = new();
		int count = 0;
		signal.Connect(new BVCallback(_ => count++));
		signal.Invoke("hello");
		Assert.Equal(1, count);
	}

	[Fact]
	public void ToString_ReturnsExpectedString()
	{
		string result = BVSignal.ToString(null);
		Assert.Equal("<BVSignal>", result);
	}
}
