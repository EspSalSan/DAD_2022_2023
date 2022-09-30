﻿using BankServer;
using Grpc.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace BankServer
{
    public class ServerService : BankServerService.BankServerServiceBase
    {
        private int balance;
        public ServerService()
        {
            this.balance = 0;
        }

        public override Task<WithdrawReply> Withdraw(
            WithdrawRequest request, ServerCallContext context)
        {
            return Task.FromResult(WithdrawMoney(request));
        }

        public WithdrawReply WithdrawMoney(WithdrawRequest request)
        {
            Console.WriteLine($"Withdraw: {request.Value}");
            this.balance -= request.Value;
            return new WithdrawReply
            {
                Value = request.Value,
                Balance = this.balance
            };
        }

        public override Task<DepositReply> Deposit(
            DepositRequest request, ServerCallContext context)
        {
            return Task.FromResult(DepositMoney(request));
        }

        public DepositReply DepositMoney(DepositRequest request)
        {
            Console.WriteLine($"Deposit: {request.Value}");
            this.balance += request.Value;
            return new DepositReply
            {
                Balance = this.balance
            };
        }

        public override Task<ReadReply> Read(
            ReadRequest request, ServerCallContext context)
        {
            return Task.FromResult(ReaddBalance(request));
        }

        public ReadReply ReaddBalance(ReadRequest request)
        {
            Console.WriteLine($"Read: {this.balance}");
            return new ReadReply
            {
                Balance = this.balance
            };
        }
    }
    class Program
    {
        static void Main(string[] args)
        {
            Server server = new Server
            {
                Services = { BankServerService.BindService(new ServerService()) },
                Ports = { new ServerPort(args[1], int.Parse(args[2]), ServerCredentials.Insecure) }
            };

            server.Start();

            Console.WriteLine("ChatServer server listening on port " + args[2]);
            Console.WriteLine("Press any key to stop the server...");
            Console.ReadKey();

            server.ShutdownAsync().Wait();

        }
    }
}
