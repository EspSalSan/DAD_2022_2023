

Boney recebe compareAndSwap do Banco (fica no saco)

Boney sabem quem é o lider para este slot (config)
Boney Lider comeca paxos
faz prepare se nao encontrar nada propoem do saco
learner responde ao pedido quando tiver uma maioria de accepted

quando um servidor recebe uma grpc call fica preso num while(frozen)
é preciso uma queue ? podemos deixar varios pedidos ficarem presos na propria task
é preciso garantir ordem

historico de slots (quem foi eleito, wtv)

banco lista de comandos executados e pendentes

quando o slot começa dá append de variaveis inicializadas a -1
