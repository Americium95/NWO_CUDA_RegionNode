#include "cuda_runtime.h"
#include "device_launch_parameters.h"

#include <stdio.h> 

//메모리 쓰기
cudaError_t menCopyWithCuda(const float4* a, unsigned int arraySize);
//거리연산
cudaError_t disFilterWithCuda(float *c, const float4 start, unsigned int size);
int memFreeWithCuda();

//커널함수 정의
__global__ void disFilterKernel(float *c,float4 start, const float4 *a, unsigned int size)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= size) return;  // 남는 스레드 무시
    if (abs(a[i].x - start.x) + abs(a[i].y - start.y) < 5)
    {
        c[i] = abs(a[i].z - start.z + (a[i].x - start.x) * 2560) + abs(a[i].w - start.w - (a[i].y - start.y) * 2560);
    }
    else
        c[i] = 100000;
}


//CUDA디버깅용
int main()
{
    const int arraySize = 5;
    const float4 a[arraySize] = { {1, 2, 3}, {4, 5, 6}, {7, 8, 9}, {10, 11, 12}, {13, 14, 15} };
    const float4 b[arraySize] = { {15,14,13}, {12,11,10}, {9,8,7}, {6,5,4}, {3,2,1} };
    float c[arraySize] = { -1 };

    // Add vectors in parallel.
    cudaError_t cudaStatus = menCopyWithCuda(a,arraySize);
    if (cudaStatus != cudaSuccess) {
        fprintf(stderr, "addWithCuda failed!");
        return 1;
    }
    cudaStatus = disFilterWithCuda(c,b[0], arraySize);
    if (cudaStatus != cudaSuccess) {
        fprintf(stderr, "addWithCuda failed!");
        return 1;
    }

    // 결과 출력
    for (int i = 0; i < arraySize; ++i) {
        printf("%d:{%.1f, %.1f, %.1f} + {%.1f, %.1f, %.1f} = {%.1f}\n", i,
            a[i].x, a[i].y, a[i].z,
            b[0].x, b[0].y, b[0].z,
            c[i]);
    }

    // cudaDeviceReset must be called before exiting in order for profiling and
    // tracing tools such as Nsight and Visual Profiler to show complete traces.
    cudaStatus = cudaDeviceReset();
    if (cudaStatus != cudaSuccess) {
        fprintf(stderr, "cudaDeviceReset failed!");
        return 1;
    }

    return 0;
}


// Perform GPU computation
float4* dev_a;
float* dev_c;
cudaError_t cudaStatus;

extern "C" __declspec(dllexport) int cudaMemCopy(float4* a, int arraySize)
{
    menCopyWithCuda(a,arraySize);
    return 0;
}


extern "C" __declspec(dllexport) float* exportCppFunctionAdd(float* dst, float4 start, int arraySize)
{
    // Perform GPU computation
    cudaError_t cudaStatus = disFilterWithCuda(dst, start, arraySize);
    return dst;
    /*
    // Error checking (optional)
    if (cudaStatus != cudaSuccess) {
        fprintf(stderr, "disFilterWithCuda failed: %s\n", cudaGetErrorString(cudaStatus));
        // Returning an error code to indicate failure
        return -1;
    }

    // Return the required value
    return 88888888;
    */
}

extern "C" __declspec(dllexport) int cudaMemFree(float4* a, int arraySize)
{
    memFreeWithCuda();
    return 0;
}


cudaError_t menCopyWithCuda(const float4* a, unsigned int arraySize)
{
    cudaError_t cudaStatus;

    cudaStatus = cudaMalloc((void**)&dev_a, arraySize * sizeof(float4));
    if (cudaStatus != cudaSuccess) {
        fprintf(stderr, "cudaMalloc failed!0");
        cudaFree(dev_a);
        return cudaStatus;
    }


    // Allocate GPU buffers for three vectors (two input, one output)    .
    cudaStatus = cudaMalloc((void**)&dev_c, arraySize * sizeof(float4));
    if (cudaStatus != cudaSuccess) {
        fprintf(stderr, "cudaMalloc failed!1");
        memFreeWithCuda();
    }

    // gpu버퍼로 입력
    cudaStatus = cudaMemcpy(dev_a, a, arraySize * sizeof(float4), cudaMemcpyHostToDevice);
    if (cudaStatus != cudaSuccess) {
        fprintf(stderr, "cudaMemcpy failed!2");
        memFreeWithCuda();
        return cudaStatus;
    }

    return cudaStatus;
}

// Helper function for using CUDA to add vectors in parallel.
cudaError_t disFilterWithCuda(float *c, const float4 start, unsigned int size)
{
    unsigned int threadsPerBlock = 256;
    unsigned int blocks = (size + threadsPerBlock - 1) / threadsPerBlock;
    // 커널실행
    disFilterKernel << <blocks, threadsPerBlock >> > (dev_c, start, dev_a, size);
    //disFilterKernel << <1, size >> > (dev_c, start, dev_a);

    // Check for any errors launching the kernel
    cudaStatus = cudaGetLastError();
    if (cudaStatus != cudaSuccess) {
        fprintf(stderr, "addKernel launch failed: %s\n", cudaGetErrorString(cudaStatus));
        memFreeWithCuda();
        return cudaStatus;
    }

    // cudaDeviceSynchronize waits for the kernel to finish, and returns
    // any errors encountered during the launch.
    cudaStatus = cudaDeviceSynchronize();
    if (cudaStatus != cudaSuccess) {
        fprintf(stderr, "cudaDeviceSynchronize returned error code %d after launching addKernel!\n", cudaStatus);
        memFreeWithCuda();
    }

    // 출력결과 메모리로 복사
    cudaStatus = cudaMemcpy(c, dev_c, size * sizeof(float), cudaMemcpyDeviceToHost);
    if (cudaStatus != cudaSuccess) {
        fprintf(stderr, "cudaMemcpy failed!3");
        memFreeWithCuda();
        return cudaStatus;
    }

    //메모리 해제
//Error:
    //cudaFree(dev_c);
    //cudaFree(dev_a);

    return cudaStatus;
}

int memFreeWithCuda()
{
    cudaFree(dev_c);
    cudaFree(dev_a);
    return 0;
}

// Helper function for using CUDA to add vectors in parallel.
/*cudaError_t disFilterWithCuda(float4* c, const float4 start, const float4* a, unsigned int size)
{
    float4* dev_c;
    float4* dev_a;

    cudaError_t cudaStatus;

    // Choose which GPU to run on, change this on a multi-GPU system.
    cudaStatus = cudaSetDevice(0);
    if (cudaStatus != cudaSuccess) {
        fprintf(stderr, "cudaSetDevice failed!  Do you have a CUDA-capable GPU installed?");
        goto Error;
    }

    // Allocate GPU buffers for three vectors (two input, one output)    .
    cudaStatus = cudaMalloc((void**)&dev_c, size * sizeof(float4));
    if (cudaStatus != cudaSuccess) {
        fprintf(stderr, "cudaMalloc failed!");
        goto Error;
    }

    cudaStatus = cudaMalloc((void**)&dev_a, size * sizeof(float4));
    if (cudaStatus != cudaSuccess) {
        fprintf(stderr, "cudaMalloc failed!");
        goto Error;
    }

    // gpu버퍼로 입력
    cudaStatus = cudaMemcpy(dev_a, a, size * sizeof(float4), cudaMemcpyHostToDevice);
    if (cudaStatus != cudaSuccess) {
        fprintf(stderr, "cudaMemcpy failed!");
        goto Error;
    }

    // 커널실행
    disFilterKernel << <1, size >> > (dev_c, start, dev_a);

    // Check for any errors launching the kernel
    cudaStatus = cudaGetLastError();
    if (cudaStatus != cudaSuccess) {
        fprintf(stderr, "addKernel launch failed: %s\n", cudaGetErrorString(cudaStatus));
        goto Error;
    }

    // cudaDeviceSynchronize waits for the kernel to finish, and returns
    // any errors encountered during the launch.
    cudaStatus = cudaDeviceSynchronize();
    if (cudaStatus != cudaSuccess) {
        fprintf(stderr, "cudaDeviceSynchronize returned error code %d after launching addKernel!\n", cudaStatus);
        goto Error;
    }

    // 출력결과 메모리로 복사
    cudaStatus = cudaMemcpy(c, dev_c, size * sizeof(float4), cudaMemcpyDeviceToHost);
    if (cudaStatus != cudaSuccess) {
        fprintf(stderr, "cudaMemcpy failed!");
        goto Error;
    }

    //메모리 해제
Error:
    cudaFree(dev_c);
    cudaFree(dev_a);

    return cudaStatus;
}*/