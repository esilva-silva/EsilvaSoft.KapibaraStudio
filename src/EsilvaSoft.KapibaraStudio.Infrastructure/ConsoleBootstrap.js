(function (hostCall, hostOutput, hostMessage, hostEnvironment, hostUuid, hostUuidJson, profiles, primary, database, captureName, maxDocuments) {
    'use strict';
    const sources = new WeakMap();
    const cursors = new WeakMap();
    const stringify = value => JSON.stringify(value, function(key, v) {
        const original = this[key];
        if (original instanceof Date) return {$date: original.toISOString()};
        if (original instanceof RegExp) return {$regularExpression: {pattern: original.source, options: original.flags}};
        return typeof v === 'bigint' ? {$numberLong: String(v)} : v;
    });
    function bsonNumber(value) {
        if (!value || typeof value !== 'object') return value;
        const key = ['$numberInt', '$numberLong', '$numberDouble', '$numberDecimal'].find(k => Object.hasOwn(value, k));
        if (!key || Object.keys(value).length !== 1) return value;
        Object.defineProperty(value, 'valueOf', {value: () => {
            if (key === '$numberDecimal') throw new Error('Decimal128 exige conversão explícita; use String(valor).');
            const number = Number(value[key]);
            return key === '$numberLong' && !Number.isSafeInteger(number) ? BigInt(value[key]) : number;
        }});
        Object.defineProperty(value, 'toString', {value: () => value[key]});
        return Object.freeze(value);
    }
    const parse = text => JSON.parse(text, (_, value) => bsonNumber(value));
    const isEmpty = value => value === null || value === undefined || (typeof value === 'object' && Object.keys(value).length === 0);
    // A non-empty projection may omit stored fields; the IDE then never offers the result as an editable copy.
    function isProjected(method, args) {
        if (method === 'find') return !isEmpty(args[1]) || (args[2] !== null && typeof args[2] === 'object' && !isEmpty(args[2].project));
        return method === 'findOne' && !isEmpty(args[1]);
    }
    function send(source, method, args) {
        const envelope = parse(hostCall(stringify({...source, method, argumentsJson: stringify(args)})));
        if (envelope.error) throw new Error(envelope.error);
        const reply = envelope.result;
        let value = reply.value;
        if (method === 'countDocuments' || method === 'estimatedDocumentCount') {
            const number = Number(value);
            if (Number.isSafeInteger(number)) value = number;
        }
        if (value !== null && typeof value === 'object') sources.set(value, {...source, method, truncated: reply.truncated === true, projected: isProjected(method, args), reply: envelope.reply});
        return value;
    }
    function CursorProxy(source, method, args) {
        const options = {limit: maxDocuments, skip: 0};
        const cursor = Object.create(null);
        for (const key of ['sort', 'skip', 'limit', 'project', 'hint', 'collation', 'comment', 'batchSize', 'maxTimeMS']) {
            cursor[key] = value => {
                if (key === 'skip' || key === 'limit') {
                    if (!Number.isSafeInteger(value) || value < 0) throw new Error(key + ' exige inteiro não negativo.');
                    options[key] = key === 'limit' ? Math.min(value || maxDocuments, maxDocuments) : value;
                } else options[key] = value;
                return cursor;
            };
        }
        cursor.toArray = () => send(source, method, [...args, options]);
        cursors.set(cursor, {source, materialize: cursor.toArray});
        return Object.freeze(cursor);
    }
    function CollectionProxy(source) {
        const collection = Object.create(null);
        collection.find = (filter = {}, projection = null) => CursorProxy(source, 'find', [filter, projection]);
        collection.aggregate = (pipeline = []) => CursorProxy(source, 'aggregate', [pipeline]);
        for (const method of ['findOne', 'countDocuments', 'estimatedDocumentCount', 'distinct', 'insertOne', 'insertMany',
            'updateOne', 'updateMany', 'replaceOne', 'deleteOne', 'deleteMany', 'drop', 'createIndex', 'dropIndex', 'stats']) {
            collection[method] = (...args) => send(source, method, args);
        }
        return Object.freeze(collection);
    }
    function DatabaseProxy(profileId, name) {
        const source = {profileId, database: name, collection: ''};
        const api = Object.create(null);
        api.getCollection = collection => CollectionProxy({...source, collection: String(collection)});
        api.getSiblingDB = database => DatabaseProxy(profileId, String(database));
        api.getName = () => name;
        api.dropDatabase = () => send(source, 'dropDatabase', []);
        api.createCollection = (name, options = {}) => send({...source, collection: String(name)}, 'createCollection', [options]);
        api.stats = () => send(source, 'databaseStats', []);
        return new Proxy(api, {get: (target, key) => typeof key === 'symbol' ? undefined :
            Object.hasOwn(target, key) ? target[key] : api.getCollection(key)});
    }
    function ConnectionProxy(profileId) {
        const api = Object.create(null);
        api.getDatabase = name => DatabaseProxy(profileId, String(name));
        return new Proxy(api, {get: (target, key) => typeof key === 'symbol' ? undefined :
            Object.hasOwn(target, key) ? target[key] : api.getDatabase(key)});
    }
    const connections = Object.create(null);
    function getConnection(name) {
        const matches = profiles.filter(p => p.name === name);
        if (matches.length !== 1) throw new Error(matches.length ? 'Nome de conexão ambíguo: ' + name : 'Conexão não cadastrada: ' + name);
        return connections[name] || (connections[name] = ConnectionProxy(matches[0].id));
    }
    const ConnectionPool = new Proxy(Object.create(null), {get: (_, name) => typeof name === 'symbol' ? undefined : getConnection(name)});
    const ENV = Object.freeze({get: key => {
        const reply = JSON.parse(hostEnvironment(String(key)));
        if (reply.error) throw new Error(reply.error);
        return reply.value;
    }});
    const console = Object.freeze(Object.fromEntries(['log', 'warn', 'error'].map(level => [level,
        (...args) => { hostMessage(level + ': ' + args.map(v => typeof v === 'string' ? v : stringify(v)).join(' ')); }])));
    const expose = (name, value) => Object.defineProperty(globalThis, name, {value, writable: false, configurable: false});
    expose('db', DatabaseProxy(primary, database));
    expose('getConnection', getConnection); expose('ConnectionPool', ConnectionPool); expose('ENV', ENV); expose('console', console);
    const hostValue = text => {
        const reply = JSON.parse(text);
        if (reply.error) throw new Error(reply.error);
        return reply.value;
    };
    // EJSON.parse also accepts ObjectId and the IDE UUID constructors; strings that only look like identifiers stay strings.
    expose('EJSON', Object.freeze({parse: text => {
        text = String(text);
        return parse(text.includes('UUID') || text.includes('ObjectId') || text.includes('Date') ? hostValue(hostUuidJson(text)) : text);
    }, stringify}));
    for (const name of ['UUID', 'CGUUID', 'JUUID', 'GUUID']) {
        expose(name, function(value) {
            if (typeof value !== 'string') throw new Error(name + '(...) exige um texto com 32 dígitos hexadecimais, com ou sem hífens.');
            const binary = hostValue(hostUuid(name, value)).$binary;
            return Object.freeze({$binary: Object.freeze(binary)});
        });
    }
    expose('ObjectId', function(value) {
        if (typeof value !== 'string' || !/^[a-fA-F0-9]{24}$/.test(value)) throw new Error('ObjectId exige 24 dígitos hexadecimais.');
        return {$oid: value};
    });
    expose('NumberLong', function(value) {
        if (typeof value === 'number' && !Number.isSafeInteger(value)) throw new Error('Use texto para NumberLong fora da precisão segura.');
        return bsonNumber({$numberLong: String(value)});
    });
    expose('NumberDecimal', function(value) { return bsonNumber({$numberDecimal: String(value)}); });
    const NativeDate = Date;
    const dateArguments = args => args.length === 1 && typeof args[0] === 'string' && /^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}(\.\d{1,3})?$/.test(args[0])
        ? [Number(JSON.parse(hostValue(hostUuidJson('Date(' + JSON.stringify(args[0]) + ')'))).$date.$numberLong)] : args;
    expose('Date', new Proxy(NativeDate, {
        apply: (target, receiver, args) => args.length === 0 ? target() : new target(...dateArguments(args)),
        construct: (target, args) => new target(...dateArguments(args))
    }));
    expose('ISODate', function(value) { return value === undefined ? new Date() : new Date(value); });
    expose(captureName, value => {
        if (value === undefined) return;
        if (cursors.has(value)) value = cursors.get(value).materialize();
        hostOutput(stringify({value, source: value !== null && typeof value === 'object' ? sources.get(value) : null}));
    });
})(__hostCall, __hostOutput, __hostMessage, __hostEnvironment, __hostUuid, __hostUuidJson, __profiles, __primary, __database, __captureName, __maxDocuments);
delete globalThis.__hostCall; delete globalThis.__hostOutput; delete globalThis.__hostMessage; delete globalThis.__hostEnvironment;
delete globalThis.__hostUuid; delete globalThis.__hostUuidJson;
delete globalThis.__profiles; delete globalThis.__primary; delete globalThis.__database; delete globalThis.__captureName; delete globalThis.__maxDocuments;
