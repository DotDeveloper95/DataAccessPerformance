﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Peregrine
{
    public sealed class AwaitableSocket : INotifyCompletion, IDisposable
    {
        private static readonly Action Sentinel = () => { };

        private readonly SocketAsyncEventArgs _socketAsyncEventArgs;
        private readonly Socket _socket;

        private Action _continuation;

        public AwaitableSocket(SocketAsyncEventArgs socketAsyncEventArgs, Socket socket)
        {
            _socketAsyncEventArgs = socketAsyncEventArgs;
            _socket = socket;

            socketAsyncEventArgs.Completed
                += (_, __) =>
                    {
                        var continuation
                            = _continuation
                              ?? Interlocked.CompareExchange(ref _continuation, Sentinel, null);

                        continuation?.Invoke();
                    };
        }

        public bool IsConnected => _socket.Connected;
        public int BytesTransferred => _socketAsyncEventArgs.BytesTransferred;

        public void SetBuffer(byte[] buffer, int offset, int count)
        {
            _socketAsyncEventArgs.SetBuffer(buffer, offset, count);
        }

        public void SetBuffer(Memory<byte> memory)
            => _socketAsyncEventArgs.SetBuffer(memory);

        public AwaitableSocket ConnectAsync(CancellationToken cancellationToken)
        {
            Reset();

            if (!_socket.ConnectAsync(_socketAsyncEventArgs))
            {
                IsCompleted = true;
            }

            cancellationToken.Register(Cancel);

            void Cancel()
            {
                if (!_socket.Connected)
                {
                    _socket.Dispose();
                }
            }

            return this;
        }

        public AwaitableSocket ReceiveAsync()
        {
            Reset();

            if (!_socket.ReceiveAsync(_socketAsyncEventArgs))
            {
                IsCompleted = true;
            }

            return this;
        }

        public AwaitableSocket SendAsync()
        {
            Reset();

            if (!_socket.SendAsync(_socketAsyncEventArgs))
            {
                IsCompleted = true;
            }

            return this;
        }

        private void Reset()
        {
            IsCompleted = false;
            _continuation = null;
        }

        public AwaitableSocket GetAwaiter()
        {
            return this;
        }

        public bool IsCompleted { get; private set; }

        public void OnCompleted(Action continuation)
        {
            if (_continuation == Sentinel
                || Interlocked.CompareExchange(
                    ref _continuation, continuation, null) == Sentinel)
            {
                Task.Run(continuation);
            }
        }

        public void GetResult()
        {
            if (_socketAsyncEventArgs.SocketError != SocketError.Success)
            {
                throw new SocketException((int)_socketAsyncEventArgs.SocketError);
            }
        }

        public void Dispose()
        {
            if (_socket != null)
            {
                if (_socket.Connected)
                {
                    _socket.Shutdown(SocketShutdown.Both);
                    _socket.Close();
                }

                _socket.Dispose();
            }

            _socketAsyncEventArgs?.Dispose();
        }
    }
}
