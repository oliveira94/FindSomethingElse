﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace remoting_interfaces
{
    public interface Ipcs
    {
        void create_replica(int rep_factor, string replica_URL, string WhatOperator, int op_id);
    }
    public interface Ipuppet_master
    {

    }
    //needs an interface to allow operators communicate with pcs!!!!
}
