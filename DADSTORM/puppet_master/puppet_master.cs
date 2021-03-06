﻿using System;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Tcp;
using System.Collections;
using System.Threading;
using System.Collections.Generic;
using remoting_interfaces;
using System.Text.RegularExpressions;
using System.IO;
using System.Runtime.Serialization.Formatters;

namespace puppet_master
{
    static class Puppet_master
    {
        static string semantics;
        static string loggin_level = "light"; //nivel de logging for defeito
        public delegate void RemoteAsyncDelegate(int rep_factor, string replica_URL, string whatoperator, string op_id); //irá apontar para a função a ser chamada assincronamente
        public delegate void RemoteAsyncDelegateSet_Start(string op_spec_in, int firstTime, string op_id, string logging_level);
        public delegate void RemoteAsyncDelegateNext_Op(string url, string route);
        public delegate void RemoteAsyncDelegateFreeze();
        public delegate void RemoteAsyncDelegateUnfreeze();
        public delegate void RemoteAsyncDelegateCrash();
        public delegate void RemoteAsyncDelegateInterval(int time);
        public delegate void RemoteAsyncDelegateStatus();

        static AsyncCallback funcaoCallBack; //irá chamar uma função quando a função assincrona terminar
        static AsyncCallback funcaoCallBackSet_Start;
        static AsyncCallback funcaoCallBackNext_Op;
        static AsyncCallback funcaoCallBackFreeze;
        static AsyncCallback funcaoCallBackUnfreeze;
        static AsyncCallback funcaoCallBackCrash;
        static AsyncCallback funcaoCallBackInterval;
        static AsyncCallback funcaoCallBackStatus;

        static Ipcs pcs_obj;
        static IOperator op_obj;
  
        public struct Operator
        {
            public string operator_id;
            public string input_ops;
            public int rep_factor;
            public string routing;
            public string address;
            public string operator_spec;
        }

        static List<Operator> op_list = new List<Operator>(); //lista de todos os operadores
        static IDictionary<string, IOperator> dic; // dicionario com todos os objetos remotos criados em todas as replicas
        static List<string> commands_from_file = new List<string>();

        static private puppet_master_object pmo;

        static void Main(string[] args)
        {
            read_conf_file();
            create_replicas();
            next_Operator();

            using (System.IO.StreamWriter file = new System.IO.StreamWriter(@"..\..\..\log.txt", true))
            {
                file.WriteLine("||-----------------Log date:" + DateTime.Now + "-----------------||");
            }

            IDictionary propBag = new Hashtable();
            propBag["port"] = 10001;
            propBag["typeFilterLevel"] = TypeFilterLevel.Full;
            propBag["name"] = "puppet_channel";

            BinaryServerFormatterSinkProvider serverProv = new BinaryServerFormatterSinkProvider();
            serverProv.TypeFilterLevel = TypeFilterLevel.Full;

            TcpChannel channel = new TcpChannel(propBag,null, serverProv);
            ChannelServices.RegisterChannel(channel, false);

            pmo = new puppet_master_object();

            RemotingServices.Marshal(pmo, "puppet_master", typeof(puppet_master_object));

            Console.WriteLine("Input command");

            foreach (string word in commands_from_file)
            {
                read_command(word);
            }

            while (true)
            {
                string command = Console.ReadLine();
                read_command(command);
            }       
        }

        static void read_conf_file()
        {
            string line;

            System.IO.StreamReader file = new System.IO.StreamReader(@"..\..\..\conf_file.txt");

            while ((line = file.ReadLine()) != null)
            {
                string[] words = line.Split(' ');

                if (line.StartsWith("Start")
                      || line.StartsWith("Freeze")
                      || line.StartsWith("Unfreeze")
                      || line.StartsWith("Crash")
                      || line.StartsWith("Interval")
                      || line.StartsWith("Wait"))
                {
                    commands_from_file.Add(line);
                }
                else if (line.StartsWith("%"))
                {

                }
                else
                {
                    foreach (string word in words)
                    {
                        if (word == "Semantics")
                        {
                            semantics = words[1];
                        }
                        else if (word == "LoggingLevel")
                        {
                            loggin_level = words[1];
                        }
                        else if (word.StartsWith("OP") && word == words[0]) // se a palavra for começar por OP e for a primeira palavra da lista de palavras
                        {
                            Operator op = new Operator();
                            op.operator_id = words[0];
                            op.input_ops = words[3];
                            op.rep_factor = Int32.Parse(words[6]);
                            op.routing = words[8];
                            op.address = words[10];

                            for (int i = 1; i < op.rep_factor; i++) //para o apanhar o numero de URLs especificado em rep_factor 
                            {
                                op.address = op.address + words[10 + i];
                            }

                            op.operator_spec = words[10 + op.rep_factor + 2]; // guardamos o tipo de operador 
                            if (!op.operator_spec.Equals("COUNT") && !op.operator_spec.Equals("DUP")) // Se o tipo for diferente de "count" significa que ainda falta concatenar os parametros
                            {
                                op.operator_spec = op.operator_spec + "," + words[(10 + op.rep_factor + 2) + 1]; //concatenamos o tipo de operador com os seus parametros
                            }
                            op_list.Add(op);
                        }
                    }
                }
            }

            dic = new Dictionary<string, IOperator>();
            foreach(Operator op in op_list) // são criados objetos em todas as replicas, depois são guardados no dicionario
            {
                string[] urls = op.address.Split(',');
                foreach(string url in urls)
                {
                    op_obj = (IOperator)Activator.GetObject(typeof(IOperator), url);
                    dic.Add(url, op_obj);
                }
            }
        }

        static public void create_replicas()
        {
            pcs_obj = (Ipcs)Activator.GetObject(typeof(Ipcs), "tcp://localhost:10000/pcs");

            funcaoCallBack = new AsyncCallback(OnExit);//aponta para a função de retorno da função assincrona
            RemoteAsyncDelegate dele = new RemoteAsyncDelegate(pcs_obj.create_replica);//aponta para a função a ser chamada assincronamente

            foreach (Operator op in op_list) 
            {
                IAsyncResult result = dele.BeginInvoke(op.rep_factor, op.address, op.operator_spec, op.operator_id, funcaoCallBack, null); 

            }
        }

        static public void next_Operator() //envia informação a cada operador sobre o operador seguinte
        {

            for (int i = 0; i < (op_list.Count - 1); i++)
            {
                string[] words = op_list[i].address.Split(','); //cria uma lista com os URLs de todos do operador atual
                foreach (string url in words)
                {
                    funcaoCallBackNext_Op = new AsyncCallback(OnExitNext_Op);//aponta para a função de retorno da função assincrona
                    RemoteAsyncDelegateNext_Op dele = new RemoteAsyncDelegateNext_Op(dic[url].next_op);//aponta para a função a ser chamada assincronamente
                    IAsyncResult result = dele.BeginInvoke(op_list[i + 1].address, op_list[i + 1].routing, funcaoCallBackSet_Start, null);

                    //dic[url].next_op(op_list[i + 1].address, op_list[i + 1].routing); //envia ao operador os URLs do operador downstream
                }
            }

        }

        private static string routing(string urls, string routing)
        {
            string[] words = urls.Split(',');       // contem todos os URls do  downstream operador 
            if (routing.Equals("primary"))
            {
                return words[0];
            }
            else if (routing.Equals("random"))
            {
                Random rnd = new Random();
                int rep = rnd.Next((words.Length)); // gera um numero de 0 até ao replication factor do operador
                return words[rep];
            }
            else
            {
                return words[0];
            }
            return "error";
        }

        static void read_command(string command)
        {
            List<string> output = new List<string>();
            string[] words = command.Split(' ');
            try
            {
                if (command.StartsWith("Start")) // se o comando começar com "start"
                {
                    foreach (Operator op in op_list) //percorremos todos os operadores na lista de operadores
                    {
                        if (op.operator_id.Equals(words[1]))// se encontrarmos o operador especificado no comando
                        {
                            if (words[1].Equals("OP1"))// se o operador encontrado for o primeiro
                            {
                                funcaoCallBackSet_Start = new AsyncCallback(OnExitSet_Start);//aponta para a função de retorno da função assincrona
                                RemoteAsyncDelegateSet_Start dele = new RemoteAsyncDelegateSet_Start(dic[routing(op.address, op.routing)].set_start);//aponta para a função a ser chamada assincronamente
                                IAsyncResult result = dele.BeginInvoke(op.operator_spec, 0, op.operator_id,loggin_level, funcaoCallBackSet_Start, null);                             
                            }
                            else // caso o operador encontrado não seja o primeiro
                            {
                                string[] rep = op.address.Split(','); // dividimos os seus URls
                            
                                foreach (string url in rep) //para cada URl das replicas do operador encontrado
                                {
                                    funcaoCallBackSet_Start = new AsyncCallback(OnExitSet_Start);//aponta para a função de retorno da função assincrona
                                    RemoteAsyncDelegateSet_Start dele = new RemoteAsyncDelegateSet_Start(dic[url].set_start);//aponta para a função a ser chamada assincronamente
                                    IAsyncResult result = dele.BeginInvoke(op.operator_spec, 1, op.operator_id,loggin_level, funcaoCallBackSet_Start, null);                              
                                }
                            }
                            break;
                        }
                    }
                }
                else if (command.StartsWith("Freeze OP"))
                {

                    foreach (Operator op in op_list)
                    {
                        if (op.operator_id.Equals(words[1]))
                        {
                            string[] rep = op.address.Split(','); // dividimos os seus URls
                            string url = rep[Int32.Parse(words[2])];

                            funcaoCallBackFreeze = new AsyncCallback(OnExitFreeze);//aponta para a função de retorno da função assincrona
                            RemoteAsyncDelegateFreeze dele = new RemoteAsyncDelegateFreeze(dic[url].set_freeze);//aponta para a função a ser chamada assincronamente
                            IAsyncResult result = dele.BeginInvoke(funcaoCallBackFreeze, null);
                        }
                    }
                }
                else if (command.StartsWith("Unfreeze OP"))
                {
                    foreach (Operator op in op_list)
                    {
                        if (op.operator_id.Equals(words[1]))
                        {
                            string[] rep = op.address.Split(','); // dividimos os seus URls
                            string url = rep[Int32.Parse(words[2])];

                            funcaoCallBackUnfreeze = new AsyncCallback(OnExitUnfreeze);//aponta para a função de retorno da função assincrona
                            RemoteAsyncDelegateUnfreeze dele = new RemoteAsyncDelegateUnfreeze(dic[url].set_unfreeze);//aponta para a função a ser chamada assincronamente
                            IAsyncResult result = dele.BeginInvoke(funcaoCallBackUnfreeze, null);
                        }
                    }
                }
                else if (command.StartsWith("Crash OP"))
                {
                    foreach (Operator op in op_list)
                    {
                        if (op.operator_id.Equals(words[1]))
                        {
                            string[] rep = op.address.Split(','); // dividimos os seus URls
                            string url = rep[Int32.Parse(words[2])];

                            funcaoCallBackCrash = new AsyncCallback(OnExitCrash);//aponta para a função de retorno da função assincrona
                            RemoteAsyncDelegateCrash dele = new RemoteAsyncDelegateCrash(dic[url].crash);//aponta para a função a ser chamada assincronamente
                            IAsyncResult result = dele.BeginInvoke(funcaoCallBackCrash, null);
                        }
                    }
                }
                else if (command.StartsWith("Wait"))
                {
                    Thread.Sleep(Int32.Parse(Regex.Match(words[1], @"\d+").Value));
                }
                else if (command.StartsWith("Interval"))
                {
                    foreach (Operator op in op_list)
                    {
                        if (op.operator_id.Equals(words[1]))
                        {
                            string[] rep = op.address.Split(','); // dividimos os seus URls
                            foreach (string url in rep)
                            {
                                funcaoCallBackInterval = new AsyncCallback(OnExitInterval);//aponta para a função de retorno da função assincrona
                                RemoteAsyncDelegateInterval dele = new RemoteAsyncDelegateInterval(dic[url].Interval);//aponta para a função a ser chamada assincronamente
                                IAsyncResult result = dele.BeginInvoke(Int32.Parse(words[2]), funcaoCallBackInterval, null);
                            }
                        }
                    }
                }
                else if (command.StartsWith("Status"))
                {
                    foreach (Operator op in op_list)
                    {
                        if (op.operator_id.Equals(words[1]))
                        {
                            string[] rep = op.address.Split(','); // dividimos os seus URls
                            foreach (string url in rep)
                            {
                                funcaoCallBackStatus = new AsyncCallback(OnExitStatus);//aponta para a função de retorno da função assincrona
                                RemoteAsyncDelegateStatus dele = new RemoteAsyncDelegateStatus(dic[url].Status);//aponta para a função a ser chamada assincronamente
                                IAsyncResult result = dele.BeginInvoke(funcaoCallBackStatus, null);
                            }
                        }
                    }
                }
            }
            catch (System.Net.Sockets.SocketException)
            {
                Console.WriteLine("Operator crashed.");
            }
        }

        public static void OnExit(IAsyncResult ar) { }

        public static void OnExitSet_Start(IAsyncResult ar) { }

        public static void OnExitNext_Op(IAsyncResult ar) { }

        public static void OnExitFreeze(IAsyncResult ar) { }

        public static void OnExitUnfreeze(IAsyncResult ar) { }

        public static void OnExitCrash(IAsyncResult ar) { }

        public static void OnExitInterval(IAsyncResult ar) { }

        public static void OnExitStatus(IAsyncResult ar) { }
    }

    public class puppet_master_object : MarshalByRefObject, Ipuppet_master
    {

        static object logLock = new object();

        public void log(string log_entry)
        {
            Monitor.Enter(logLock);
            try
            {
                using (System.IO.StreamWriter file = new System.IO.StreamWriter(@"..\..\..\log.txt", true))
                {
                    file.WriteLine(log_entry);
                }
            }
            finally
            {
                Monitor.Exit(logLock);
            }
        }
    }
}