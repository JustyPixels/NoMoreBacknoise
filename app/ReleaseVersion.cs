using System;
namespace NoMoreBacknoise;
internal static class ReleaseVersion {
    public static bool IsNewer(string candidate,string current) {
        var a=candidate.TrimStart('v').Split('-',2);var b=current.TrimStart('v').Split('-',2);
        if(!Version.TryParse(a[0],out var av) || !Version.TryParse(b[0],out var bv))return false;
        if(av!=bv)return av>bv;
        if(a.Length==1)return b.Length>1;if(b.Length==1)return false;
        var ap=a[1].Split('.');var bp=b[1].Split('.');
        for(var i=0;i<Math.Min(ap.Length,bp.Length);i++) {
            if(ap[i]==bp[i])continue;
            var an=int.TryParse(ap[i],out var ai);var bn=int.TryParse(bp[i],out var bi);
            return an&&bn?ai>bi:an!=bn?!an:string.CompareOrdinal(ap[i],bp[i])>0;
        }
        return ap.Length>bp.Length;
    }
}
