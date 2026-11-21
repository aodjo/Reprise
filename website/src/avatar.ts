import { decode, encode } from 'fast-png';

/** Decodes a bounded static PNG and re-encodes pixels without embedded metadata. */
export async function normalizeAvatar(bytes:ArrayBuffer):Promise<Uint8Array> {
  const data=new Uint8Array(bytes),view=new DataView(bytes);
  if(data.length<45||![137,80,78,71,13,10,26,10].every((value,index)=>data[index]===value))throw new Error('invalid_avatar');
  if(view.getUint32(8)!==13||String.fromCharCode(...data.subarray(12,16))!=='IHDR')throw new Error('invalid_avatar');
  const width=view.getUint32(16),height=view.getUint32(20),channels=data[25]===6?4:data[25]===2?3:0;
  if(!width||!height||width>512||height>512||data[24]!==8||!channels||data[26]||data[27]||data[28])throw new Error('invalid_avatar');
  const chunks:Uint8Array<ArrayBuffer>[]=[data.subarray(0,8)],compressed:Uint8Array<ArrayBuffer>[]=[];
  let offset=8,ended=false;
  while(offset+12<=data.length){
    const length=view.getUint32(offset),end=offset+length+12;
    if(end>data.length)throw new Error('invalid_avatar');
    const type=String.fromCharCode(...data.subarray(offset+4,offset+8));
    if(type==='IHDR'&&offset!==8)throw new Error('invalid_avatar');
    if(type==='IHDR'||type==='IDAT'||type==='IEND')chunks.push(data.subarray(offset,end));
    else if(!(type.charCodeAt(0)&32))throw new Error('invalid_avatar');
    if(type==='IDAT')compressed.push(data.subarray(offset+8,offset+8+length));
    offset=end;
    if(type==='IEND'){if(length||offset!==data.length)throw new Error('invalid_avatar');ended=true;break;}
  }
  if(!ended||!compressed.length)throw new Error('invalid_avatar');
  const expected=height*(width*channels+1),reader=new Blob(compressed).stream().pipeThrough(new DecompressionStream('deflate')).getReader();
  let inflated=0;
  try{
    for(;;){const chunk=await reader.read();if(chunk.done)break;inflated+=chunk.value.byteLength;if(inflated>expected)throw new Error('invalid_avatar');}
    if(inflated!==expected)throw new Error('invalid_avatar');
  }finally{await reader.cancel().catch(()=>{});reader.releaseLock();}
  const clean=new Uint8Array(chunks.reduce((total,chunk)=>total+chunk.length,0));let cursor=0;
  for(const chunk of chunks){clean.set(chunk,cursor);cursor+=chunk.length;}
  const image=decode(clean,{checkCrc:true});
  return encode({width:image.width,height:image.height,data:image.data,channels:image.channels,depth:8},{zlib:{level:1}});
}
